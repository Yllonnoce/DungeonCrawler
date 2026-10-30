#!/usr/bin/env bash
# Turn the three client exports in build/ into installable archives under dist/, each with a small setup program that
# offers Install or Uninstall and, on install, an optional launcher on the Desktop:
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
cat > "$stage/linux/$NAME/setup.sh" <<'EOF'
#!/usr/bin/env bash
# DungeonCrawler setup for the current user: no root needed, the .NET runtime is inside the build.
#   ./setup.sh                      menu: Install / Uninstall
#   ./setup.sh install [--desktop|--no-desktop]   install without the menu (asks about the Desktop launcher unless told)
#   ./setup.sh uninstall
# Install copies this folder to ~/.local/opt/DungeonCrawler, adds an app-menu entry and the "dungeoncrawler" command,
# and can put a launcher on the Desktop. Uninstall removes all of that; saves and settings stay in
# ~/.local/share/godot/app_userdata/DungeonCrawler.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dest="${HOME}/.local/opt/DungeonCrawler"
apps="${HOME}/.local/share/applications"
desktop_dir="$(xdg-user-dir DESKTOP 2>/dev/null || echo "${HOME}/Desktop")"
launcher="dungeoncrawler.desktop"

write_launcher() {
  cat > "$1" <<DESKTOP
[Desktop Entry]
Type=Application
Name=DungeonCrawler
Comment=A first-person party dungeon crawler
Exec=$dest/DungeonCrawler.x86_64
Path=$dest
Icon=applications-games
Terminal=false
Categories=Game;RolePlaying;
DESKTOP
  chmod +x "$1"
}

do_install() {
  local want_desktop="$1"
  mkdir -p "$dest" "$apps" "${HOME}/.local/bin"
  cp -r "$here"/. "$dest/"
  chmod +x "$dest/DungeonCrawler.x86_64" "$dest/setup.sh"
  ln -sf "$dest/DungeonCrawler.x86_64" "${HOME}/.local/bin/dungeoncrawler"
  write_launcher "$apps/$launcher"
  update-desktop-database "$apps" 2>/dev/null || true
  if [ "$want_desktop" = ask ]; then
    read -r -p "Put a DungeonCrawler launcher on the Desktop? [Y/n] " answer
    case "${answer:-Y}" in [Nn]*) want_desktop=no ;; *) want_desktop=yes ;; esac
  fi
  if [ "$want_desktop" = yes ]; then
    mkdir -p "$desktop_dir"
    write_launcher "$desktop_dir/$launcher"
    gio set "$desktop_dir/$launcher" metadata::trusted true 2>/dev/null || true   # GNOME asks to "Allow Launching" otherwise
    echo "Desktop launcher: $desktop_dir/$launcher"
  fi
  echo "Installed to $dest. Launch it from the app menu (DungeonCrawler) or run: dungeoncrawler"
  echo "To remove it later: $dest/setup.sh uninstall"
}

do_uninstall() {
  rm -rf "$dest" "$apps/$launcher" "$desktop_dir/$launcher" "${HOME}/.local/bin/dungeoncrawler"
  update-desktop-database "$apps" 2>/dev/null || true
  echo "Removed DungeonCrawler. Saves and settings stay in ~/.local/share/godot/app_userdata/DungeonCrawler; delete that folder too if you want them gone."
}

case "${1:-}" in
  install)
    case "${2:-}" in --desktop) do_install yes ;; --no-desktop) do_install no ;; *) do_install ask ;; esac ;;
  uninstall) do_uninstall ;;
  "")
    echo "DungeonCrawler setup"
    if [ -d "$dest" ]; then echo "  (an install exists at $dest)"; fi
    echo "  1) Install"
    echo "  2) Uninstall"
    echo "  3) Quit"
    read -r -p "Choose [1-3]: " pick
    case "$pick" in
      1) do_install ask ;;
      2) do_uninstall ;;
      *) echo "Nothing done." ;;
    esac ;;
  *) echo "usage: $0 [install [--desktop|--no-desktop] | uninstall]" >&2; exit 2 ;;
esac
EOF
# the two plain commands from earlier releases keep working
printf '#!/usr/bin/env bash\nexec "$(dirname "$0")/setup.sh" install "$@"\n' > "$stage/linux/$NAME/install.sh"
printf '#!/usr/bin/env bash\nexec "$(dirname "$0")/setup.sh" uninstall\n' > "$stage/linux/$NAME/uninstall.sh"
chmod +x "$stage/linux/$NAME/setup.sh" "$stage/linux/$NAME/install.sh" "$stage/linux/$NAME/uninstall.sh" "$stage/linux/$NAME/$NAME.x86_64"
tar -C "$stage/linux" -czf "dist/$NAME-$VERSION-linux-x86_64.tar.gz" "$NAME"

# ---- Windows: the exe, its data folder and Setup.bat (Install / Uninstall menu, Desktop shortcut on request)
mkdir -p "$stage/windows"
cp -r build/windows/. "$stage/windows/"
# CRLF line endings: cmd.exe misreads a batch file with bare LF in some constructs
sed 's/$/\r/' > "$stage/windows/Setup.bat" <<'EOF'
@echo off
setlocal
rem DungeonCrawler setup for the current user (no administrator rights needed).
rem   Setup.bat                menu: Install / Uninstall
rem   Setup.bat install        install (asks about a Desktop shortcut)
rem   Setup.bat uninstall      remove the install and its shortcuts
rem Install copies this folder to %LOCALAPPDATA%\Programs\DungeonCrawler and adds a Start Menu shortcut, plus a Desktop
rem shortcut if you want one. Saves and settings stay in %APPDATA%\Godot\app_userdata\DungeonCrawler.
set "HERE=%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\DungeonCrawler"
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\DungeonCrawler.lnk"
set "DESKTOP=%USERPROFILE%\Desktop\DungeonCrawler.lnk"

if /i "%~1"=="install" goto install
if /i "%~1"=="uninstall" goto uninstall
if not "%~1"=="" echo usage: Setup.bat [install ^| uninstall] & exit /b 2

echo DungeonCrawler setup
if exist "%DEST%\DungeonCrawler.exe" echo   (an install exists at %DEST%)
echo   1^) Install
echo   2^) Uninstall
echo   3^) Quit
choice /c 123 /n /m "Choose [1-3]: "
if errorlevel 3 echo Nothing done.& exit /b 0
if errorlevel 2 goto uninstall
goto install

:install
echo Installing to %DEST% ...
if not exist "%DEST%" mkdir "%DEST%"
robocopy "%HERE%." "%DEST%" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 echo Copy failed.& exit /b 1
call :shortcut "%STARTMENU%"
choice /c YN /n /m "Put a DungeonCrawler shortcut on the Desktop? [Y/N] "
if errorlevel 2 goto installed
call :shortcut "%DESKTOP%"
echo Desktop shortcut: %DESKTOP%
:installed
echo Installed. Start it from the Start Menu (DungeonCrawler) or run "%DEST%\DungeonCrawler.exe".
echo To remove it later run "%DEST%\Setup.bat" uninstall
echo SmartScreen may warn the first time because the build is unsigned: "More info" then "Run anyway".
exit /b 0

:uninstall
del /q "%STARTMENU%" 2>nul
del /q "%DESKTOP%" 2>nul
if /i "%HERE:~0,-1%"=="%DEST%" (
  rem running from inside the install folder: delete it after this script ends
  start "" /min cmd /c "ping -n 2 127.0.0.1 >nul & rmdir /s /q "%DEST%""
) else (
  rmdir /s /q "%DEST%" 2>nul
)
echo Removed DungeonCrawler. Saves and settings stay in %%APPDATA%%\Godot\app_userdata\DungeonCrawler; delete that folder too if you want them gone.
exit /b 0

:shortcut
powershell -NoProfile -ExecutionPolicy Bypass -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%~1'); $s.TargetPath='%DEST%\DungeonCrawler.exe'; $s.WorkingDirectory='%DEST%'; $s.Description='A first-person party dungeon crawler'; $s.Save()"
exit /b 0
EOF
( cd "$stage/windows" && zip -qr "$OLDPWD/dist/$NAME-$VERSION-windows-x86_64.zip" . )

# ---- macOS: Godot's zip holds the .app; add Setup.command (Install / Uninstall menu, Desktop alias on request)
mkdir -p "$stage/macos"
( cd "$stage/macos" && unzip -q "$OLDPWD/build/macos/$NAME.zip" )
cat > "$stage/macos/Setup.command" <<'EOF'
#!/bin/bash
# DungeonCrawler setup. Double-click, or in Terminal:
#   ./Setup.command                menu: Install / Uninstall
#   ./Setup.command install        install (asks about a Desktop alias)
#   ./Setup.command uninstall
# Install copies DungeonCrawler.app to /Applications (or ~/Applications when /Applications is not writable), clears the
# download quarantine so the ad-hoc-signed app opens, and can put an alias on the Desktop. Saves and settings stay in
# ~/Library/Application Support/Godot/app_userdata/DungeonCrawler.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
app="DungeonCrawler.app"
if [ -w /Applications ]; then dest="/Applications/$app"; else mkdir -p "$HOME/Applications"; dest="$HOME/Applications/$app"; fi
alias_path="$HOME/Desktop/DungeonCrawler"

do_install() {
  local want_desktop="$1"
  [ -d "$here/$app" ] || { echo "$app is not next to this script."; exit 1; }
  rm -rf "$dest"
  cp -R "$here/$app" "$dest"
  xattr -dr com.apple.quarantine "$dest" 2>/dev/null || true
  if [ "$want_desktop" = ask ]; then
    read -r -p "Put a DungeonCrawler alias on the Desktop? [Y/n] " answer
    case "${answer:-Y}" in [Nn]*) want_desktop=no ;; *) want_desktop=yes ;; esac
  fi
  if [ "$want_desktop" = yes ]; then
    rm -f "$alias_path"
    osascript -e "tell application \"Finder\" to make alias file to (POSIX file \"$dest\") at (POSIX file \"$HOME/Desktop\")" >/dev/null \
      && mv -f "$HOME/Desktop/$app" "$alias_path" 2>/dev/null || true
    echo "Desktop alias: $alias_path"
  fi
  echo "Installed to $dest. Open it from Launchpad or Applications."
  echo "To remove it later: run this Setup.command again and choose Uninstall."
}

do_uninstall() {
  rm -rf "/Applications/$app" "$HOME/Applications/$app" "$alias_path" "$HOME/Desktop/$app"
  echo "Removed DungeonCrawler. Saves and settings stay in ~/Library/Application Support/Godot/app_userdata/DungeonCrawler; delete that folder too if you want them gone."
}

case "${1:-}" in
  install) case "${2:-}" in --desktop) do_install yes ;; --no-desktop) do_install no ;; *) do_install ask ;; esac ;;
  uninstall) do_uninstall ;;
  "")
    echo "DungeonCrawler setup"
    [ -d "$dest" ] && echo "  (an install exists at $dest)"
    echo "  1) Install"
    echo "  2) Uninstall"
    echo "  3) Quit"
    read -r -p "Choose [1-3]: " pick
    case "$pick" in 1) do_install ask ;; 2) do_uninstall ;; *) echo "Nothing done." ;; esac ;;
  *) echo "usage: $0 [install [--desktop|--no-desktop] | uninstall]" >&2; exit 2 ;;
esac
EOF
chmod +x "$stage/macos/Setup.command"
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

Saves and settings live in the user data folder of each system (Godot's app_userdata/DungeonCrawler) and survive an uninstall.
EOF
( cd dist && sha256sum *.tar.gz *.zip > SHA256SUMS )
ls -la dist
