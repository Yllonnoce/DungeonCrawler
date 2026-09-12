#!/usr/bin/env bash
# Set up a Debian/Ubuntu machine to run the server from this clone: runtime libraries, Godot 4.7.2 .NET and the
# .NET 8 SDK (both user-local, no sudo needed except for apt), then the first import and build.
# Safe to run again: anything already in place is skipped.   bash deploy/gce/setup_source.sh
set -euo pipefail
cd "$(dirname "$0")/../.."
GODOT_VERSION=${GODOT_VERSION:-4.7.2}
GODOT_DIR="$HOME/.local/opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64"
GODOT_BIN="$GODOT_DIR/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64"
export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

echo "== runtime libraries"
sudo apt-get update -qq
sudo apt-get install -y -qq --no-install-recommends ca-certificates curl unzip libfontconfig1 libgl1 libxcursor1 libxinerama1 libxrandr2 libxi6 libasound2 libpulse0 libudev1

echo "== Godot $GODOT_VERSION .NET"
if [ -x "$GODOT_BIN" ]; then echo "already at $GODOT_BIN"; else
  mkdir -p "$HOME/.local/opt/godot" "$HOME/.local/bin"
  ZIP="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
  curl -fL --progress-bar -o "/tmp/$ZIP" "https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-stable/$ZIP"
  unzip -q -o "/tmp/$ZIP" -d "$HOME/.local/opt/godot"
  rm -f "/tmp/$ZIP"
fi
ln -sf "$GODOT_BIN" "$HOME/.local/bin/godot"

echo "== .NET 8 SDK"
if "$HOME/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^8\.'; then echo "already installed"; else
  curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0 --install-dir "$HOME/.dotnet"
fi

echo "== global .NET install location (a binary with the port-80 capability runs in secure mode and ignores DOTNET_ROOT)"
sudo mkdir -p /etc/dotnet
echo "$HOME/.dotnet" | sudo tee /etc/dotnet/install_location > /dev/null

echo "== PATH in ~/.bashrc"
grep -q 'DOTNET_ROOT="$HOME/.dotnet"' "$HOME/.bashrc" 2>/dev/null || cat >> "$HOME/.bashrc" <<'RC'
# DungeonCrawler: user-local Godot and .NET
export PATH="$HOME/.local/bin:$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
RC

echo "== versions"
godot --version | head -1
dotnet --version

echo "== first import and build (a few minutes on a small machine)"
[ -d .godot/imported ] && echo "assets already imported" || godot --headless --import --path . > /tmp/godot-import.log 2>&1 || true
dotnet build DungeonCrawler.csproj -v q --nologo
echo
echo "Ready. Start with: bash deploy/gce/start.sh   (or PORT=7777 ... for an unprivileged port)"
