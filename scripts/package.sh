#!/usr/bin/env bash
# Turn the three client exports in build/ into installable archives under dist/, each with the setup program from
# installer/<os>/ (Install / Uninstall menu, optional Desktop launcher) and the Host script that runs the game as a server:
#   dist/DungeonCrawler-<version>-linux-x86_64.tar.gz   the binary, its data folder, setup.sh (menu) plus install.sh / uninstall.sh
#   dist/DungeonCrawler-<version>-windows-x86_64.zip    DungeonCrawler.exe, its data folder and Setup.bat (menu; copies to %LOCALAPPDATA%\Programs)
#   dist/DungeonCrawler-<version>-macos-universal.zip   DungeonCrawler.app (ad-hoc signed, not notarized) and Setup.command (menu; copies to Applications)
# plus dist/SHA256SUMS and dist/INSTALL.txt. Run scripts/export_all.sh --no-ship (or the three --export-release lines in
# docs/development.md) first; VERSION=1.2 overrides the version, which defaults to the short git hash.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=${VERSION:-$(git rev-parse --short HEAD 2>/dev/null || echo dev)}
NAME=DungeonCrawler
for need in build/linux/$NAME.x86_64 build/windows/$NAME.exe build/macos/$NAME.zip; do
  [ -e "$need" ] || { echo "missing $need: export first (scripts/export_all.sh --no-ship)" >&2; exit 1; }
done
rm -rf dist; mkdir -p dist
stage=$(mktemp -d); trap 'rm -rf "$stage"' EXIT

# ---- Linux: a folder with setup.sh (Install / Uninstall menu, Desktop launcher on request)
mkdir -p "$stage/linux/$NAME"
cp -r build/linux/. "$stage/linux/$NAME/"
cp installer/linux/setup.sh installer/linux/host.sh "$stage/linux/$NAME/"
# the two plain commands from earlier releases keep working
printf '#!/usr/bin/env bash\nexec "$(dirname "$0")/setup.sh" install "$@"\n' > "$stage/linux/$NAME/install.sh"
printf '#!/usr/bin/env bash\nexec "$(dirname "$0")/setup.sh" uninstall\n' > "$stage/linux/$NAME/uninstall.sh"
chmod +x "$stage/linux/$NAME/setup.sh" "$stage/linux/$NAME/host.sh" "$stage/linux/$NAME/install.sh" "$stage/linux/$NAME/uninstall.sh" "$stage/linux/$NAME/$NAME.x86_64"
tar -C "$stage/linux" -czf "dist/$NAME-$VERSION-linux-x86_64.tar.gz" "$NAME"

# ---- Windows: the exe, its data folder and Setup.bat (Install / Uninstall menu, Desktop shortcut on request)
mkdir -p "$stage/windows"
cp -r build/windows/. "$stage/windows/"
# Setup.bat and Host.bat are stored with CRLF line endings (.gitattributes): cmd.exe misreads bare LF in some constructs
cp installer/windows/Setup.bat installer/windows/Host.bat "$stage/windows/"
( cd "$stage/windows" && zip -qr "$OLDPWD/dist/$NAME-$VERSION-windows-x86_64.zip" . )

# ---- macOS: Godot's zip holds the .app; add Setup.command (Install / Uninstall menu, Desktop alias on request)
mkdir -p "$stage/macos"
( cd "$stage/macos" && unzip -q "$OLDPWD/build/macos/$NAME.zip" )
cp installer/macos/Setup.command installer/macos/Host.command "$stage/macos/"
chmod +x "$stage/macos/Setup.command" "$stage/macos/Host.command"
( cd "$stage/macos" && zip -qry "$OLDPWD/dist/$NAME-$VERSION-macos-universal.zip" . )

cat > dist/INSTALL.txt <<EOF
DungeonCrawler $VERSION

Each archive carries a small setup program with two choices, Install or Uninstall. Install also asks whether to put a
launcher on the Desktop. Nothing needs administrator rights; the .NET runtime is inside the build.

Linux (x86_64)
  tar xzf $NAME-$VERSION-linux-x86_64.tar.gz
  ./$NAME/setup.sh                 # menu: Install (app-menu entry, "dungeoncrawler" command, optional Desktop launcher) or Uninstall
  (./$NAME/setup.sh install --desktop and ./$NAME/setup.sh uninstall skip the questions; or just run ./$NAME/$NAME.x86_64 from the folder)

Windows (x86_64)
  Unzip $NAME-$VERSION-windows-x86_64.zip anywhere and double-click Setup.bat: Install copies the game to
  %LOCALAPPDATA%\\Programs\\DungeonCrawler with a Start Menu shortcut and, if you say yes, a Desktop shortcut; Uninstall removes them.
  (Or run $NAME.exe straight from the unzipped folder.) SmartScreen may warn because the build is unsigned: "More info" > "Run anyway".

macOS (universal, Apple silicon and Intel)
  Unzip $NAME-$VERSION-macos-universal.zip and double-click Setup.command: Install copies $NAME.app to Applications,
  clears the download quarantine (the app is ad-hoc signed, not notarized) and can put an alias on the Desktop; Uninstall removes them.
  If macOS refuses to open Setup.command, right-click it > Open once.

Hosting a game for friends: run host.sh (Linux), Host.bat (Windows) or Host.command (macOS) from the unpacked folder and
give the players the address it prints. docs/server-setup.md covers a server that runs around the clock in the cloud.

Saves and settings live in the user data folder of each system (Godot's app_userdata/DungeonCrawler) and survive an uninstall.
EOF
( cd dist && sha256sum *.tar.gz *.zip > SHA256SUMS )
ls -la dist
