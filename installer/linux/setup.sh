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
