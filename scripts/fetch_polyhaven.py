#!/usr/bin/env python3
"""Download 1k JPG texture sets (diffuse, GL normal, roughness) from Poly Haven (CC0) into assets/textures/<id>/.

Usage: scripts/fetch_polyhaven.py id [id ...]
Files are named <id>_{diff,nor,rough}_1k.jpg, which is what DungeonBuilder.Material expects.
Run `godot --headless --import --path .` afterwards so Godot imports them.
"""
import json, os, sys, urllib.request

UA = {"User-Agent": "DungeonCrawler-asset-fetch/1.0"}

def get(url, timeout):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout)

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "textures")
API = "https://api.polyhaven.com/files/"

def pick(files, want):
    for key, value in files.items():
        if key.lower().replace("_", "") == want:
            return value
    return None

def fetch(asset):
    out = os.path.join(ROOT, asset)
    if os.path.exists(os.path.join(out, f"{asset}_diff_1k.jpg")):
        print(f"{asset}: already present")
        return True
    try:
        with get(API + asset, 30) as r:
            files = json.load(r)
    except Exception as e:
        print(f"{asset}: not found ({e})")
        return False
    wanted = {"diff": pick(files, "diffuse") or pick(files, "diff"), "nor": pick(files, "norgl"), "rough": pick(files, "rough")}
    os.makedirs(out, exist_ok=True)
    for suffix, entry in wanted.items():
        if not entry:
            print(f"{asset}: no {suffix} map")
            continue
        url = entry["1k"]["jpg"]["url"]
        dest = os.path.join(out, f"{asset}_{suffix}_1k.jpg")
        with get(url, 120) as r, open(dest, "wb") as f:
            f.write(r.read())
        print(f"{asset}: {suffix} {os.path.getsize(dest) // 1024} KB")
    return True

if __name__ == "__main__":
    ok = [a for a in sys.argv[1:] if fetch(a)]
    print("fetched:", " ".join(ok))
