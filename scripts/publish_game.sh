#!/usr/bin/env bash
# Publish the player installers as a GitHub release, so anyone can download the game for their system from the
# repository's Releases page without building anything:
#   scripts/publish_game.sh [tag]        default tag: game-YYYYMMDD-HHMM; needs `gh auth login` once
# Runs the three exports and the packaging first (scripts/export_all.sh --no-ship, scripts/package.sh) unless
# SKIP_BUILD=1 says dist/ is already what you want. The release is marked "latest", which is what the README's
# download link points at; the dedicated server has its own server-* releases and does not take that mark.
set -euo pipefail
cd "$(dirname "$0")/.."
TAG=${1:-game-$(date -u +%Y%m%d-%H%M)}
if [ -z "${SKIP_BUILD:-}" ]; then
  scripts/export_all.sh --no-ship
  scripts/package.sh
fi
ls dist/*.tar.gz dist/*.zip dist/INSTALL.txt dist/SHA256SUMS > /dev/null || { echo "nothing in dist/ to publish (run scripts/export_all.sh --no-ship and scripts/package.sh)"; exit 1; }
VERSION=$(git rev-parse --short HEAD)
NOTES=$(cat <<EOF
DungeonCrawler for Linux, Windows and macOS (built from commit $VERSION).

Download the file for your system, unpack it, and run the setup program inside it:
- Linux: \`DungeonCrawler-*-linux-x86_64.tar.gz\`, then \`./DungeonCrawler/setup.sh\` (menu: Install or Uninstall; Install can add a Desktop launcher)
- Windows: \`DungeonCrawler-*-windows-x86_64.zip\`, then double-click \`Setup.bat\` (SmartScreen may warn because the build is unsigned: "More info", then "Run anyway")
- macOS: \`DungeonCrawler-*-macos-universal.zip\`, then double-click \`Setup.command\` (right-click, Open the first time)

INSTALL.txt repeats these steps; SHA256SUMS lets you check a download. Saves live in your user data folder and survive reinstalling.
EOF
)
gh release create "$TAG" dist/*.tar.gz dist/*.zip dist/INSTALL.txt dist/SHA256SUMS --title "DungeonCrawler $TAG" --notes "$NOTES" --latest
echo "published: gh release view $TAG   (players: https://github.com/$(gh repo view --json nameWithOwner -q .nameWithOwner)/releases/latest)"
