#!/usr/bin/env bash
# Get DungeonCrawler onto this computer with one command. No git, no build tools, no GitHub account.
#
#   curl -fsSL https://raw.githubusercontent.com/Yllonnoce/DungeonCrawler/main/get.sh | bash
#
# Linux (x86_64) and macOS (Apple silicon or Intel). Windows users: get.bat next to this file does the same.
# It finds the newest game release on GitHub, downloads the archive for this system into ~/Downloads/DungeonCrawler-<tag>,
# checks it against the release's SHA256SUMS, unpacks it and runs the setup program inside, which offers Install or
# Uninstall and can put a launcher on the Desktop. Running it again later installs the newest release over the old one.
#
# Saved to disk, it also takes the setup program's own words:
#   bash get.sh install --desktop      install without questions, with a Desktop launcher
#   bash get.sh install --no-desktop
#   bash get.sh uninstall
# DUNGEONCRAWLER_TAG=game-20261030-2000 bash get.sh   installs that release instead of the newest one.
set -euo pipefail

REPO=${DUNGEONCRAWLER_REPO:-Yllonnoce/DungeonCrawler}
API="https://api.github.com/repos/$REPO/releases"
PAGE="https://github.com/$REPO/releases"

say()  { printf '%s\n' "$*"; }
fail() { printf 'get.sh: %s\n' "$*" >&2; exit 1; }
need() { command -v "$1" >/dev/null 2>&1 || fail "this needs the '$1' command, which is not installed."; }

need curl
case "$(uname -s)" in
  Linux)
    case "$(uname -m)" in x86_64|amd64) ;; *) fail "the Linux build is for x86_64 computers; this one is $(uname -m)." ;; esac
    need tar
    suffix="-linux-x86_64.tar.gz"; setup="DungeonCrawler/setup.sh" ;;
  Darwin)
    need unzip
    suffix="-macos-universal.zip"; setup="Setup.command" ;;
  *) fail "this script is for Linux and macOS. On Windows, download get.bat from https://github.com/$REPO and double-click it." ;;
esac

# ---- which release
fetch() { curl -fsSL -H 'Accept: application/vnd.github+json' -H 'User-Agent: DungeonCrawler-get' "$1"; }
if [ -n "${DUNGEONCRAWLER_TAG:-}" ]; then
  tag=$DUNGEONCRAWLER_TAG
else
  say "Looking up the newest DungeonCrawler release ..."
  listing=$(fetch "$API?per_page=50") || fail "could not reach GitHub ($API). Are you online? You can also download by hand from $PAGE"
  tag=$(printf '%s' "$listing" | grep -o '"tag_name": *"game-[^"]*"' | head -1 | sed 's/.*"\(game-[^"]*\)"/\1/' || true)
  [ -n "$tag" ] || fail "no game release has been published yet (only server builds). Check $PAGE later."
fi
release=$(fetch "$API/tags/$tag") || fail "release $tag was not found at $PAGE"
urls=$(printf '%s' "$release" | grep -o '"browser_download_url": *"[^"]*"' | sed 's/.*"\(https\?:[^"]*\)"/\1/' || true)
archive_url=$(printf '%s\n' "$urls" | grep -- "$suffix\$" | head -1 || true)
sums_url=$(printf '%s\n' "$urls" | grep '/SHA256SUMS$' | head -1 || true)
[ -n "$archive_url" ] || fail "release $tag has no file ending in $suffix. See $PAGE/tag/$tag"
archive=${archive_url##*/}

# ---- download into ~/Downloads/DungeonCrawler-<tag>
downloads=${XDG_DOWNLOAD_DIR:-$HOME/Downloads}
[ -d "$downloads" ] || downloads=$HOME
dir="$downloads/DungeonCrawler-$tag"
mkdir -p "$dir"
cd "$dir"

checksum() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1
  elif command -v shasum >/dev/null 2>&1; then shasum -a 256 "$1" | cut -d' ' -f1
  else echo skip; fi
}
expected=
if [ -n "$sums_url" ]; then
  expected=$(fetch "$sums_url" | grep -- " \*\?$archive\$" | cut -d' ' -f1 || true)
fi

if [ -f "$archive" ] && [ -n "$expected" ] && [ "$(checksum "$archive")" = "$expected" ]; then
  say "Already downloaded: $dir/$archive"
else
  say "Downloading $archive (about 500 MB) to $dir ..."
  curl -fL --progress-bar -o "$archive.part" "$archive_url" || fail "the download failed. Try again, or download by hand from $PAGE/tag/$tag"
  mv -f "$archive.part" "$archive"
  if [ -n "$expected" ]; then
    actual=$(checksum "$archive")
    if [ "$actual" = skip ]; then say "(no sha256 tool here, checksum not verified)"
    elif [ "$actual" != "$expected" ]; then rm -f "$archive"; fail "the downloaded file is damaged (checksum mismatch). Run this again."
    else say "Checksum OK."; fi
  fi
fi

# ---- unpack and hand over to the setup program inside
say "Unpacking ..."
case "$archive" in
  *.tar.gz) tar xzf "$archive" ;;
  *.zip)    unzip -qo "$archive" ;;
esac
chmod +x "$setup"
command -v xattr >/dev/null 2>&1 && xattr -d com.apple.quarantine "$setup" 2>/dev/null || true
say "Starting the setup program ($dir/$setup) ..."
say
# When this script arrives through a pipe (curl | bash), the setup program's questions must read from the terminal.
if [ $# -gt 0 ]; then
  "./$setup" "$@"
elif [ -t 0 ]; then
  "./$setup"
elif ( : </dev/tty ) 2>/dev/null; then
  "./$setup" </dev/tty
else
  say "(no terminal to ask questions on: installing with a Desktop launcher)"
  "./$setup" install --desktop
fi
