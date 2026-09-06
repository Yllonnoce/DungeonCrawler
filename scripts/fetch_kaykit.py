#!/usr/bin/env python3
"""Download files from a KayKit (CC0) asset pack on GitHub into assets/models/<folder>/.

Usage: scripts/fetch_kaykit.py <repo> <repo-subdir> <folder> file [file ...]
e.g.   scripts/fetch_kaykit.py KayKit-Dungeon-Remastered-1.0 addons/kaykit_dungeon_remastered/Assets/gltf kaykit_dungeon key.gltf.glb
Run `godot --headless --import --path .` afterwards.
"""
import os, sys, urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "models")
RAW = "https://raw.githubusercontent.com/KayKit-Game-Assets/{repo}/main/{path}"
UA = {"User-Agent": "DungeonCrawler-asset-fetch/1.0"}

def fetch(repo, subdir, folder, name):
    dest = os.path.join(ROOT, folder, name)
    if os.path.exists(dest):
        print(f"  {name}: present"); return True
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    url = RAW.format(repo=repo, path=f"{subdir}/{name}")
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=120) as r, open(dest, "wb") as f:
            f.write(r.read())
    except Exception as e:
        print(f"  {name}: failed ({e})"); return False
    print(f"  {name}: {os.path.getsize(dest) // 1024} KB"); return True

if __name__ == "__main__":
    repo, subdir, folder, *names = sys.argv[1:]
    ok = [n for n in names if fetch(repo, subdir, folder, n)]
    print("fetched:", len(ok), "of", len(names))
