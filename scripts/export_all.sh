#!/usr/bin/env bash
# Export the game client for Linux, Windows and macOS from this machine (Godot cross-exports all three; the templates
# must be installed in the editor: ~/.local/share/godot/export_templates/4.7.2.stable.mono). Presets live in
# export_presets.cfg. Pass --server to also export the dedicated server bundles via export_server.sh.
#
# Before exporting, the campaign you last rebaked from Settings (user://adventures/builtin, plus the voice lines it
# uses from user://voice) is copied into the project (data/adventures/builtin, assets/voice/builtin), so the build
# plays it; --no-ship exports whatever the project already holds, BAKE_DIR=... points at another user data folder.
#   scripts/export_all.sh                 # ship the latest rebake, then build/linux, build/windows, build/macos
#   scripts/export_all.sh --server        # plus build/server and build/macos-server
#   scripts/export_all.sh --no-ship       # export the shipped campaign as it is in the repository
#   scripts/export_all.sh --ship-only     # only copy the rebake into the project (no export)
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT=${GODOT:-godot}
SHIP=1; SERVER=0; EXPORT=1
for arg in "$@"; do
  case "$arg" in
    --server) SERVER=1 ;;
    --no-ship) SHIP=0 ;;
    --ship-only) EXPORT=0 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

# ---- the rebake: the newest user://adventures/builtin/campaign.json among Godot's user data folders (the snap-hosted
# VS Code terminal keeps its own under ~/snap/code/<rev>/), unless BAKE_DIR names one
ship_rebake() {
  local dir="${BAKE_DIR:-}"
  if [ -z "$dir" ]; then
    local newest=""
    for c in ~/.local/share/godot/app_userdata/DungeonCrawler ~/snap/code/*/.local/share/godot/app_userdata/DungeonCrawler; do
      [ -f "$c/adventures/builtin/campaign.json" ] || continue
      if [ -z "$newest" ] || [ "$c/adventures/builtin/campaign.json" -nt "$newest/adventures/builtin/campaign.json" ]; then newest="$c"; fi
    done
    dir="$newest"
  fi
  if [ -z "$dir" ] || [ ! -f "$dir/adventures/builtin/campaign.json" ]; then
    echo "No rebaked campaign found under Godot's user data (looked in ~/.local/share and ~/snap/code); exporting the shipped one in data/adventures/builtin."
    return 0
  fi
  local title source floors
  title=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1])).get('Title',''))" "$dir/adventures/builtin/campaign.json")
  source=$(python3 -c "import json,sys; print(json.load(open(sys.argv[1])).get('Source',''))" "$dir/adventures/builtin/campaign.json")
  floors=$(ls "$dir"/adventures/builtin/floor_*.json 2>/dev/null | wc -l)
  echo "Shipping the rebake from $dir: \"$title\" ($source, $floors floors) -> data/adventures/builtin"
  if [ "$floors" -lt 20 ]; then echo "WARNING: only $floors of 20 floors are baked; the build designs the missing ones offline as it reaches them." >&2; fi
  mkdir -p data/adventures/builtin
  rm -f data/adventures/builtin/floor_*.json
  cp "$dir"/adventures/builtin/*.json data/adventures/builtin/
  # voice: only the lines this campaign refers to (the cache also holds lines from earlier runs)
  local copied=0 missing=0
  if [ -d "$dir/voice" ]; then
    mkdir -p assets/voice/builtin
    for id in $(grep -oh '"VoiceId": *"[0-9a-f]\{16\}"' data/adventures/builtin/*.json | grep -o '[0-9a-f]\{16\}' | sort -u); do
      if [ -f "$dir/voice/$id.wav" ]; then cp "$dir/voice/$id.wav" assets/voice/builtin/; copied=$((copied+1)); else missing=$((missing+1)); fi
    done
  fi
  echo "Voice: $copied rendered lines copied into assets/voice/builtin ($missing referenced lines have no audio yet)"
  if [ "$copied" -gt 0 ]; then
    echo "Importing the new audio so the export packs it..."
    "$GODOT" --headless --import --path . > /dev/null 2>&1 || true
  fi
}
if [ "$SHIP" = 1 ]; then ship_rebake; else echo "Exporting the campaign in data/adventures/builtin as it is (--no-ship)."; fi
if [ "$EXPORT" = 0 ]; then echo "Rebake shipped into the project; not exporting (--ship-only)."; exit 0; fi

dotnet build DungeonCrawler.csproj -c Release
mkdir -p build/linux build/windows build/macos
"$GODOT" --headless --path . --export-release "Linux" build/linux/DungeonCrawler.x86_64
"$GODOT" --headless --path . --export-release "Windows" build/windows/DungeonCrawler.exe
"$GODOT" --headless --path . --export-release "macOS" build/macos/DungeonCrawler.zip
ls -la build/linux build/windows build/macos | grep -v "^total"
if [ "$SERVER" = 1 ]; then scripts/export_server.sh; fi
echo "Exports done. macOS is ad-hoc signed, not notarized: on another Mac, right-click > Open once, or: xattr -dr com.apple.quarantine DungeonCrawler.app"
