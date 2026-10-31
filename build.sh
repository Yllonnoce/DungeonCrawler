#!/usr/bin/env bash
# Build DungeonCrawler from its source code and install it, with one command. Made for players, not only programmers:
# it fetches the tools it needs (Godot 4.7.2 .NET, its export templates, the .NET 8 SDK) into your home folder, so it
# needs no administrator rights, builds the game for this computer, then runs the setup program (Install / Uninstall
# menu, optional Desktop launcher).
#
#   git clone https://github.com/Yllonnoce/DungeonCrawler.git && cd DungeonCrawler && bash build.sh
#   or, from anywhere:  curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/build.sh | bash
#       (clones the source into ~/DungeonCrawler with git first, or updates it when it is already there)
#   Later:  bash update.sh   pulls the newest code, rebuilds and reinstalls over the current install.
#
# Linux (x86_64) and macOS (Apple silicon or Intel). Windows: build.bat. The first run downloads about 1.3 GB of tools
# and takes 20-40 minutes; later runs skip what is already in place and take a few minutes. About 6 GB of disk in all.
# Words after the command go to the setup program:  bash build.sh install --desktop    bash build.sh uninstall
set -euo pipefail

REPO=${DUNGEONCRAWLER_REPO:-Yllonnoce/DungeonCrawler}
GODOT_VERSION=4.7.2
GODOT_BUILDS="https://github.com/godotengine/godot-builds/releases/download/${GODOT_VERSION}-stable"

say()  { printf '\n== %s\n' "$*"; }
fail() { printf 'build.sh: %s\n' "$*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "this needs the '$1' command, which is not installed."; }
need curl; need unzip; need tar

case "$(uname -s)" in
  Linux)
    case "$(uname -m)" in x86_64|amd64) ;; *) fail "the game builds for x86_64 Linux; this computer is $(uname -m)." ;; esac
    OS=linux
    GODOT_ZIP="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
    GODOT_BIN="$HOME/.local/opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64"
    TEMPLATES="$HOME/.local/share/godot/export_templates/${GODOT_VERSION}.stable.mono"
    TEMPLATE_CHECK="$TEMPLATES/linux_release.x86_64" ;;
  Darwin)
    OS=macos
    GODOT_ZIP="Godot_v${GODOT_VERSION}-stable_mono_macos.universal.zip"
    GODOT_BIN="$HOME/.local/opt/godot/Godot_mono.app/Contents/MacOS/Godot"
    TEMPLATES="$HOME/Library/Application Support/Godot/export_templates/${GODOT_VERSION}.stable.mono"
    TEMPLATE_CHECK="$TEMPLATES/macos.zip" ;;
  *) fail "this script is for Linux and macOS. On Windows use build.bat." ;;
esac
TMP="$HOME/.cache/dungeoncrawler-build"; mkdir -p "$TMP"

# ---- the source: this folder if it is the repository, else a git clone in ~/DungeonCrawler (updated when it exists)
if [ -f ./DungeonCrawler.csproj ]; then :
elif [ -f "$(dirname "$0")/DungeonCrawler.csproj" ]; then cd "$(dirname "$0")"
else
  SRC=${DUNGEONCRAWLER_SRC:-$HOME/DungeonCrawler}
  if ! command -v git >/dev/null 2>&1; then
    echo "This needs git to fetch the source. Install it and run build.sh again:" >&2
    case "$OS" in
      macos) echo "  xcode-select --install        (Apple's command line tools, which include git)" >&2 ;;
      *)     echo "  sudo apt-get install -y git   (Debian, Ubuntu, Mint)     sudo dnf install git   (Fedora)" >&2 ;;
    esac
    exit 1
  fi
  if [ -d "$SRC/.git" ]; then say "Updating the source in $SRC"; git -C "$SRC" pull --ff-only
  else say "Cloning the source into $SRC (about 700 MB)"; git clone "https://github.com/$REPO.git" "$SRC"; fi
  cd "$SRC"
fi
say "Building from $(pwd)"

# ---- Godot (the game engine) and its export templates, in your home folder
if [ -x "$GODOT_BIN" ]; then echo "Godot $GODOT_VERSION .NET: already here"; else
  say "Downloading Godot $GODOT_VERSION .NET (about 60 MB)"
  mkdir -p "$HOME/.local/opt/godot"
  curl -fL --progress-bar -o "$TMP/$GODOT_ZIP" "$GODOT_BUILDS/$GODOT_ZIP"
  unzip -q -o "$TMP/$GODOT_ZIP" -d "$HOME/.local/opt/godot"; rm -f "$TMP/$GODOT_ZIP"
  [ "$OS" = macos ] && xattr -dr com.apple.quarantine "$HOME/.local/opt/godot/Godot_mono.app" 2>/dev/null || true
  [ -x "$GODOT_BIN" ] || fail "Godot did not unpack where expected ($GODOT_BIN)."
fi
if ! "$GODOT_BIN" --version >/dev/null 2>&1; then
  echo "Godot cannot start on this computer. On Debian or Ubuntu this usually fixes it:" >&2
  echo "  sudo apt-get install -y libfontconfig1 libgl1 libxcursor1 libxinerama1 libxrandr2 libxi6 libasound2 libpulse0 libudev1 libicu-dev" >&2
  fail "then run build.sh again."
fi
if [ -e "$TEMPLATE_CHECK" ]; then echo "Export templates: already here"; else
  say "Downloading the Godot export templates (about 1 GB; this is the big one)"
  curl -fL --progress-bar -o "$TMP/templates.tpz" "$GODOT_BUILDS/Godot_v${GODOT_VERSION}-stable_mono_export_templates.tpz"
  rm -rf "$TMP/templates"; mkdir -p "$TMP/templates" "$TEMPLATES"
  unzip -q "$TMP/templates.tpz" -d "$TMP/templates"
  cp -R "$TMP/templates/templates/." "$TEMPLATES/"; rm -rf "$TMP/templates" "$TMP/templates.tpz"
  [ -e "$TEMPLATE_CHECK" ] || fail "the export templates did not unpack where expected ($TEMPLATES)."
fi

# ---- the .NET 8 SDK (the game is written in C#)
have_dotnet8() { "$1" --list-sdks 2>/dev/null | grep -q '^8\.'; }
if command -v dotnet >/dev/null 2>&1 && have_dotnet8 dotnet; then echo ".NET 8 SDK: already here ($(command -v dotnet))"
elif have_dotnet8 "$HOME/.dotnet/dotnet"; then export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; echo ".NET 8 SDK: already here (~/.dotnet)"
else
  say "Downloading the .NET 8 SDK (about 200 MB) into ~/.dotnet"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$TMP/dotnet-install.sh"
  bash "$TMP/dotnet-install.sh" --channel 8.0 --install-dir "$HOME/.dotnet"
  export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
  have_dotnet8 dotnet || fail "the .NET SDK did not install (is the disk full? df -h ~)."
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# ---- import the assets, compile the code, export the game for this computer
if [ -f .godot/import-ok ]; then echo "Assets: already imported"; else
  say "Importing the game's assets (first time only, 5-15 minutes)"
  rm -rf .godot/imported
  if "$GODOT_BIN" --headless --import --path . > "$TMP/godot-import.log" 2>&1 && ! grep -q FATAL "$TMP/godot-import.log"; then touch .godot/import-ok
  else tail -5 "$TMP/godot-import.log"; fail "the asset import failed; the full log is $TMP/godot-import.log"; fi
fi
say "Compiling the game"
dotnet build DungeonCrawler.csproj -c Release -v q --nologo -clp:ErrorsOnly
say "Exporting the game for $OS"
case "$OS" in
  linux)
    mkdir -p build/linux
    "$GODOT_BIN" --headless --path . --export-release "Linux" build/linux/DungeonCrawler.x86_64 > "$TMP/godot-export.log" 2>&1 || { tail -20 "$TMP/godot-export.log"; fail "the export failed; the full log is $TMP/godot-export.log"; }
    [ -x build/linux/DungeonCrawler.x86_64 ] || { tail -20 "$TMP/godot-export.log"; fail "no game binary came out of the export; the log is $TMP/godot-export.log"; }
    cp installer/linux/setup.sh installer/linux/host.sh build/linux/; chmod +x build/linux/setup.sh build/linux/host.sh
    setup=build/linux/setup.sh ;;
  macos)
    mkdir -p build/macos
    "$GODOT_BIN" --headless --path . --export-release "macOS" build/macos/DungeonCrawler.zip > "$TMP/godot-export.log" 2>&1 || { tail -20 "$TMP/godot-export.log"; fail "the export failed; the full log is $TMP/godot-export.log"; }
    rm -rf build/macos/DungeonCrawler; mkdir -p build/macos/DungeonCrawler
    unzip -qo build/macos/DungeonCrawler.zip -d build/macos/DungeonCrawler
    cp installer/macos/Setup.command installer/macos/Host.command build/macos/DungeonCrawler/; chmod +x build/macos/DungeonCrawler/*.command
    setup=build/macos/DungeonCrawler/Setup.command ;;
esac
say "Built. Starting the setup program ($setup)"
echo
if [ $# -gt 0 ]; then "./$setup" "$@"
elif [ -t 0 ]; then "./$setup"
elif ( : </dev/tty ) 2>/dev/null; then "./$setup" </dev/tty
else echo "(no terminal to ask questions on: installing with a Desktop launcher)"; "./$setup" install --desktop; fi
