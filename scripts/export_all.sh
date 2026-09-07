#!/usr/bin/env bash
# Export the game client for Linux, Windows and macOS from this machine (Godot cross-exports all three; the templates
# must be installed in the editor: ~/.local/share/godot/export_templates/4.7.2.stable.mono). Presets live in
# export_presets.cfg. Pass --server to also export the dedicated server bundles via export_server.sh.
#   scripts/export_all.sh            # build/linux, build/windows, build/macos
#   scripts/export_all.sh --server   # plus build/server and build/macos-server
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT=${GODOT:-godot}
dotnet build DungeonCrawler.csproj -c Release
mkdir -p build/linux build/windows build/macos
"$GODOT" --headless --path . --export-release "Linux" build/linux/DungeonCrawler.x86_64
"$GODOT" --headless --path . --export-release "Windows" build/windows/DungeonCrawler.exe
"$GODOT" --headless --path . --export-release "macOS" build/macos/DungeonCrawler.zip
ls -la build/linux build/windows build/macos | grep -v "^total"
if [ "${1:-}" = "--server" ]; then scripts/export_server.sh; fi
echo "Exports done. macOS is ad-hoc signed, not notarized: on another Mac, right-click > Open once, or: xattr -dr com.apple.quarantine DungeonCrawler.app"
