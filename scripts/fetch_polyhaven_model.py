#!/usr/bin/env python3
"""Download a 1k glTF model (with its .bin and textures) from Poly Haven (CC0) into assets/models/<id>/.

Usage: scripts/fetch_polyhaven_model.py id [id ...]
Run `godot --headless --import --path .` afterwards.
"""
import json, os, sys, urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "models")
API = "https://api.polyhaven.com/files/"
UA = {"User-Agent": "DungeonCrawler-asset-fetch/1.0"}

def get(url, timeout=120):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout)

def save(url, dest):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with get(url) as r, open(dest, "wb") as f:
        f.write(r.read())
    print(f"  {os.path.relpath(dest, ROOT)} {os.path.getsize(dest) // 1024} KB")

def fetch(asset):
    try:
        with get(API + asset, 30) as r:
            files = json.load(r)
    except Exception as e:
        print(f"{asset}: not found ({e})")
        return False
    entry = files.get("gltf", {}).get("1k", {}).get("gltf")
    if not entry:
        print(f"{asset}: no 1k glTF")
        return False
    out = os.path.join(ROOT, asset)
    print(f"{asset}:")
    save(entry["url"], os.path.join(out, f"{asset}_1k.gltf"))
    for rel, inc in entry.get("include", {}).items():
        save(inc["url"], os.path.join(out, rel))
    return True

if __name__ == "__main__":
    ok = [a for a in sys.argv[1:] if fetch(a)]
    print("fetched:", " ".join(ok))
