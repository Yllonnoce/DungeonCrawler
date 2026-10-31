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
