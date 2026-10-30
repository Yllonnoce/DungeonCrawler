#!/usr/bin/env bash
# Turn the three client exports in build/ into installable archives under dist/:
#   dist/DungeonCrawler-<version>-linux-x86_64.tar.gz   the binary, its data folder and install.sh (puts a launcher in the app menu)
#   dist/DungeonCrawler-<version>-windows-x86_64.zip    DungeonCrawler.exe with its data folder: unzip anywhere and run
#   dist/DungeonCrawler-<version>-macos-universal.zip   DungeonCrawler.app (ad-hoc signed, not notarized): unzip, drag to Applications
# plus dist/SHA256SUMS and dist/INSTALL.txt. Run scripts/export_all.sh --no-ship (or the three --export-release lines in the
# README) first; VERSION=1.2 overrides the version, which defaults to the short git hash.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION=${VERSION:-$(git rev-parse --short HEAD 2>/dev/null || echo dev)}
NAME=DungeonCrawler
for need in build/linux/$NAME.x86_64 build/windows/$NAME.exe build/macos/$NAME.zip; do
  [ -e "$need" ] || { echo "missing $need: export first (scripts/export_all.sh --no-ship)" >&2; exit 1; }
done
rm -rf dist; mkdir -p dist

# ---- Linux: a folder with an installer that drops a .desktop entry for the current user
stage=$(mktemp -d); trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/$NAME"
cp -r build/linux/. "$stage/$NAME/"
cat > "$stage/$NAME/install.sh" <<'EOF'
#!/usr/bin/env bash
# Install DungeonCrawler for the current user: copies this folder to ~/.local/opt/DungeonCrawler and adds an app-menu entry.
# Run uninstall.sh to remove it. Needs nothing else: the .NET runtime is inside the build.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dest="${HOME}/.local/opt/DungeonCrawler"
mkdir -p "$dest" "${HOME}/.local/share/applications" "${HOME}/.local/bin"
cp -r "$here"/. "$dest/"
chmod +x "$dest/DungeonCrawler.x86_64"
ln -sf "$dest/DungeonCrawler.x86_64" "${HOME}/.local/bin/dungeoncrawler"
cat > "${HOME}/.local/share/applications/dungeoncrawler.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=DungeonCrawler
Comment=A first-person party dungeon crawler
Exec=$dest/DungeonCrawler.x86_64
Path=$dest
Terminal=false
Categories=Game;RolePlaying;
DESKTOP
update-desktop-database "${HOME}/.local/share/applications" 2>/dev/null || true
echo "Installed to $dest. Launch it from the app menu (DungeonCrawler) or run: dungeoncrawler"
EOF
cat > "$stage/$NAME/uninstall.sh" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
rm -rf "${HOME}/.local/opt/DungeonCrawler" "${HOME}/.local/share/applications/dungeoncrawler.desktop" "${HOME}/.local/bin/dungeoncrawler"
echo "Removed. Saves and settings stay in ~/.local/share/godot/app_userdata/DungeonCrawler; delete that folder too if you want them gone."
EOF
chmod +x "$stage/$NAME/install.sh" "$stage/$NAME/uninstall.sh" "$stage/$NAME/$NAME.x86_64"
tar -C "$stage" -czf "dist/$NAME-$VERSION-linux-x86_64.tar.gz" "$NAME"

# ---- Windows: the exe and its data folder, unzip-and-run
( cd build/windows && zip -qr "../../dist/$NAME-$VERSION-windows-x86_64.zip" . )

# ---- macOS: Godot already wrote the .app into a zip
cp build/macos/$NAME.zip "dist/$NAME-$VERSION-macos-universal.zip"

cat > dist/INSTALL.txt <<EOF
DungeonCrawler $VERSION

Linux (x86_64)
  tar xzf $NAME-$VERSION-linux-x86_64.tar.gz
  ./$NAME/install.sh        # app-menu entry and the "dungeoncrawler" command for this user
  (or just run ./$NAME/$NAME.x86_64 from the folder; uninstall.sh removes an install)

Windows (x86_64)
  Unzip $NAME-$VERSION-windows-x86_64.zip anywhere and run $NAME.exe.
  SmartScreen may warn because the build is unsigned: "More info" > "Run anyway".

macOS (universal, Apple silicon and Intel)
  Unzip $NAME-$VERSION-macos-universal.zip and drag $NAME.app to Applications.
  The app is ad-hoc signed, not notarized: the first time, right-click > Open, or run
      xattr -dr com.apple.quarantine /Applications/$NAME.app

Saves and settings live in the user data folder of each system (Godot's app_userdata/DungeonCrawler).
EOF
( cd dist && sha256sum *.tar.gz *.zip > SHA256SUMS )
ls -la dist
