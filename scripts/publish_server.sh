#!/usr/bin/env bash
# Export the dedicated server and publish it as a GitHub release asset, so a VM can fetch it with deploy/gce/fetch.sh.
# Usage: scripts/publish_server.sh [tag]      (default tag: server-YYYYMMDD-HHMM; needs `gh auth login` once)
set -euo pipefail
cd "$(dirname "$0")/.."
TAG=${1:-server-$(date -u +%Y%m%d-%H%M)}
[ -n "${SKIP_EXPORT:-}" ] || scripts/export_server.sh
[ -x build/server/DungeonCrawler.x86_64 ] || { echo "no server build in build/server"; exit 1; }
mkdir -p build
tar -C build -czf build/dungeoncrawler-server.tar.gz server
ls -la build/dungeoncrawler-server.tar.gz
gh release create "$TAG" build/dungeoncrawler-server.tar.gz --title "Dedicated server $TAG" --notes "Linux dedicated server export (commit $(git rev-parse --short HEAD)). Fetch on a VM with deploy/gce/fetch.sh." --latest=false
echo "published: gh release view $TAG"
