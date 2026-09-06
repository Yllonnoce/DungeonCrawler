#!/usr/bin/env bash
# Export the dedicated server build into build/server/ (needs the Linux export template installed in the editor).
# Falls back to the plain Linux preset when no "Linux Server" preset exists; both run with --headless.
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT=${GODOT:-godot}
mkdir -p build/server
PRESET="Linux Server"
grep -q "name=\"$PRESET\"" export_presets.cfg 2>/dev/null || PRESET="Linux"
"$GODOT" --headless --path . --export-release "$PRESET" build/server/DungeonCrawler.x86_64
ls -la build/server | head
# the macOS server: an ad-hoc-signed universal .app inside a zip, exported here too when the macOS template is installed
if grep -q 'name="macOS Server"' export_presets.cfg && ls ~/.local/share/godot/export_templates/*/macos.zip > /dev/null 2>&1; then
  mkdir -p build/macos-server
  "$GODOT" --headless --path . --export-release "macOS Server" build/macos-server/DungeonCrawler-server.zip && ls -la build/macos-server | head
fi
