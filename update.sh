#!/usr/bin/env bash
# Update DungeonCrawler to the newest code: pull the repository, rebuild and reinstall over the current install,
# keeping your choice about the Desktop launcher. Run it inside your copy of the repository, or from anywhere when the
# copy is ~/DungeonCrawler (where build.sh puts it):
#   bash update.sh
# Linux and macOS. Windows: update.bat. Those who installed a downloaded release update with get.sh instead.
set -euo pipefail
if [ -f ./DungeonCrawler.csproj ]; then :
elif [ -f "$(dirname "$0")/DungeonCrawler.csproj" ]; then cd "$(dirname "$0")"
elif [ -f "${DUNGEONCRAWLER_SRC:-$HOME/DungeonCrawler}/DungeonCrawler.csproj" ]; then cd "${DUNGEONCRAWLER_SRC:-$HOME/DungeonCrawler}"
else echo "update.sh: no copy of the repository here or in ~/DungeonCrawler. Clone it first: git clone https://github.com/Yllonnoce/DungeonCrawler.git" >&2; exit 1; fi
command -v git >/dev/null 2>&1 || { echo "update.sh: git is not installed (build.sh prints how to get it)." >&2; exit 1; }
[ -d .git ] || { echo "update.sh: $(pwd) is not a git clone (a plain download cannot be updated; clone it instead)." >&2; exit 1; }
echo "== Pulling the newest code into $(pwd)"
before=$(git rev-parse --short HEAD)
git pull --ff-only
after=$(git rev-parse --short HEAD)
if [ "$before" = "$after" ]; then echo "Already up to date ($after); rebuilding anyway."; else echo "Updated $before -> $after"; fi
# keep the Desktop launcher if there is one, do not add one if there is not
case "$(uname -s)" in
  Darwin) [ -e "$HOME/Desktop/DungeonCrawler" ] && desktop=--desktop || desktop=--no-desktop ;;
  *)      [ -e "$(xdg-user-dir DESKTOP 2>/dev/null || echo "$HOME/Desktop")/dungeoncrawler.desktop" ] && desktop=--desktop || desktop=--no-desktop ;;
esac
exec bash build.sh install "$desktop"
