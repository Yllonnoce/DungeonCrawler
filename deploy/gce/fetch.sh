#!/usr/bin/env bash
# On the server machine: pull the latest deploy scripts from git and the latest server build from the repository's
# GitHub releases, then install (or update) the systemd service. No GitHub account is needed: the repository is public.
#   First time:    git clone https://github.com/Yllonnoce/DungeonCrawler.git ~/DungeonCrawler
#   Every update:  bash ~/DungeonCrawler/deploy/gce/fetch.sh [server-YYYYMMDD-HHMM]      (default: the newest server release)
set -euo pipefail
REPO=${DUNGEONCRAWLER_REPO:-Yllonnoce/DungeonCrawler}
API="https://api.github.com/repos/$REPO/releases"
cd "$(dirname "$0")/../.."
git pull --ff-only
fetch() { curl -fsSL -H 'Accept: application/vnd.github+json' -H 'User-Agent: DungeonCrawler-fetch' "$1"; }
TAG=${1:-$(fetch "$API?per_page=50" | grep -o '"tag_name": *"server-[^"]*"' | head -1 | sed 's/.*"\(server-[^"]*\)"/\1/' || true)}
[ -n "$TAG" ] || { echo "no server-* release found at https://github.com/$REPO/releases; publish one with scripts/publish_server.sh"; exit 1; }
URL=$(fetch "$API/tags/$TAG" | grep -o '"browser_download_url": *"[^"]*dungeoncrawler-server.tar.gz"' | sed 's/.*"\(https\?:[^"]*\)"/\1/' | head -1 || true)
[ -n "$URL" ] || { echo "release $TAG has no dungeoncrawler-server.tar.gz"; exit 1; }
echo "fetching release $TAG"
rm -rf build/server build/dungeoncrawler-server.tar.gz
mkdir -p build
curl -fL --progress-bar -o build/dungeoncrawler-server.tar.gz "$URL"
tar -C build -xzf build/dungeoncrawler-server.tar.gz
sudo bash deploy/gce/install.sh build/server
