#!/usr/bin/env bash
# On the VM: pull the latest deploy scripts from git and the latest server build from the repo's GitHub releases, then install.
# First time:  sudo apt-get install -y git gh && gh auth login   (the repo is private; a token with repo read is enough)
#              git clone https://github.com/Yllonnoce/DungeonCrawler.git ~/DungeonCrawler
# Every update: bash ~/DungeonCrawler/deploy/gce/fetch.sh [tag]
set -euo pipefail
REPO=Yllonnoce/DungeonCrawler
cd "$(dirname "$0")/../.."
git pull --ff-only
TAG=${1:-$(gh release list --repo "$REPO" --limit 20 --json tagName,isLatest --jq '.[] | select(.tagName | startswith("server-")) | .tagName' | head -1)}
[ -n "$TAG" ] || { echo "no server-* release found; run scripts/publish_server.sh on the dev machine"; exit 1; }
echo "fetching release $TAG"
rm -rf build/server build/dungeoncrawler-server.tar.gz
mkdir -p build
gh release download "$TAG" --repo "$REPO" --pattern dungeoncrawler-server.tar.gz --dir build
tar -C build -xzf build/dungeoncrawler-server.tar.gz
sudo bash deploy/gce/install.sh build/server
