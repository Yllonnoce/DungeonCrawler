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
# Debian 13 mounts /tmp in RAM at half the machine's memory, which is too small to unpack the SDK (~900 MB) or the
# Godot zip on a 1 GB VM, so every temporary file goes on the home disk instead
export TMPDIR="$HOME/.cache/dungeoncrawler-setup"
mkdir -p "$TMPDIR"

echo "== runtime libraries"
sudo apt-get update -qq
sudo apt-get install -y -qq --no-install-recommends ca-certificates curl unzip libfontconfig1 libgl1 libxcursor1 libxinerama1 libxrandr2 libxi6 libasound2 libpulse0 libudev1
# .NET aborts at start without the ICU globalization library; the package name carries the ICU version (76 on Debian 13, 72 on 12)
ICU=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)
[ -n "$ICU" ] || { echo "no libicu package found in apt"; exit 1; }
sudo apt-get install -y -qq --no-install-recommends "$ICU"

echo "== swap"
# a 1 GB VM cannot compile the project without swap: it thrashes for hours instead of failing
MEM_MB=$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)
SWAP_MB=$(awk '/SwapTotal/ {print int($2/1024)}' /proc/meminfo)
if [ "$MEM_MB" -lt 3000 ] && [ "$SWAP_MB" -lt 1000 ] && [ ! -f /swapfile ]; then
  echo "adding a 2 GB swap file (${MEM_MB} MB RAM, ${SWAP_MB} MB swap)"
  sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile > /dev/null && sudo swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab > /dev/null
else echo "ok (${MEM_MB} MB RAM, ${SWAP_MB} MB swap)"; fi

echo "== Godot $GODOT_VERSION .NET"
if [ -x "$GODOT_BIN" ]; then echo "already at $GODOT_BIN"; else
  mkdir -p "$HOME/.local/opt/godot" "$HOME/.local/bin"
  ZIP="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
  curl -fL --progress-bar -o "$TMPDIR/$ZIP" "https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-stable/$ZIP"
  unzip -q -o "$TMPDIR/$ZIP" -d "$HOME/.local/opt/godot"
  rm -f "$TMPDIR/$ZIP"
fi
ln -sf "$GODOT_BIN" "$HOME/.local/bin/godot"

echo "== .NET 8 SDK"
# a download cut short leaves an SDK folder with empty files inside, so trust the runtime only when it lists itself
if "$HOME/.dotnet/dotnet" --list-runtimes 2>/dev/null | grep -q 'Microsoft.NETCore.App 8\.' \
   && ! find "$HOME/.dotnet/shared" -name '*.runtimeconfig.json' -empty | grep -q .; then echo "already installed"; else
  [ -d "$HOME/.dotnet" ] && { echo "the .NET install under ~/.dotnet is incomplete; reinstalling"; rm -rf "$HOME/.dotnet"; }
  df -h "$HOME" | tail -1 | awk '{print "disk free on home: " $4}'
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$TMPDIR/dotnet-install.sh"
  bash "$TMPDIR/dotnet-install.sh" --channel 8.0 --install-dir "$HOME/.dotnet"
  if ! "$HOME/.dotnet/dotnet" --list-runtimes | grep -q 'Microsoft.NETCore.App 8\.' \
     || find "$HOME/.dotnet/shared" -name '*.runtimeconfig.json' -empty | grep -q .; then
    echo ".NET install failed: check 'df -h $TMPDIR' and 'df -h ~' for a full disk"; exit 1
  fi
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

echo "== first import and build (10-20 minutes on a small machine)"
if [ -f .godot/import-ok ]; then echo "assets already imported"; else
  rm -rf .godot/imported
  if godot --headless --import --path . > "$TMPDIR/godot-import.log" 2>&1 && ! grep -q 'FATAL' "$TMPDIR/godot-import.log"; then touch .godot/import-ok
  else echo "asset import failed; see $TMPDIR/godot-import.log"; tail -5 "$TMPDIR/godot-import.log"; exit 1; fi
fi
dotnet build DungeonCrawler.csproj -v m --nologo -clp:NoSummary
echo
echo "Ready. Start with: bash deploy/gce/start.sh   (or PORT=7777 ... for an unprivileged port)"
