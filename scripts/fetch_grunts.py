#!/usr/bin/env python3
"""Download CC0 creature grunts into assets/audio/grunts/<set>/ (hurt_N.ogg, death_N.ogg) and log them in
assets/LICENSES.md and assets/audio/grunts/README.md. AudioManager registers them as grunt_hurt_<set> /
grunt_death_<set> and pitches each set by VoiceSets.PitchOffset, so small and medium can share clips.
Run once, then `godot --headless --path . --import` so Godot imports the files.

Source: rubberduck's "80 CC0 creature SFX" and "80 CC0 creture SFX #2" on OpenGameArt, both marked CC0.
The brief's first choice, Kenney's "Voiceover Pack: Fighter" (CC0), was checked and dropped: it holds
announcer lines ("fight", "round 1", numbers), not hurt or death grunts.
Archives are read in memory and only the named members are written out; nothing is extracted wholesale."""
import io, os, shutil, subprocess, sys, urllib.request, zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEST = os.path.join(ROOT, "assets", "audio", "grunts")
UA = {"User-Agent": "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36"}
FILES = "https://opengameart.org/sites/default/files/"
CC0 = "https://creativecommons.org/publicdomain/zero/1.0/"

# key -> (archive file on OGA, page slug, title, author)
PACKS = {
    "p1": ("80-CC0-creature-SFX_0.zip", "80-cc0-creature-sfx", "80 CC0 creature SFX", "rubberduck"),
    "p2": ("80-CC0-creature-sfx-2.zip", "80-cc0-creture-sfx-2", "80 CC0 creture SFX #2", "rubberduck"),
}

# set -> {"hurt": [(pack, member)], "death": [...]}; at most 6 hurt and 4 death per set.
# small and medium share clips (the pitch offset tells them apart); large grunts deeper, boss roars.
_SHARED_HURT = [("p1", f"hurt_0{i}.ogg") for i in range(1, 6)] + [("p1", "grunt_02.ogg")]
_SHARED_DEATH = [("p2", f"die_0{i}.ogg") for i in range(1, 5)]
SETS = {
    "small": {"hurt": _SHARED_HURT, "death": _SHARED_DEATH},
    "medium": {"hurt": _SHARED_HURT, "death": _SHARED_DEATH},
    "large": {
        "hurt": [("p1", "grunt_01.ogg"), ("p1", "grunt_03.ogg"), ("p1", "grunt_04.ogg"), ("p1", "grunt_05.ogg"),
                 ("p2", "grunt_06.ogg"), ("p2", "grunt_08.ogg")],
        "death": [("p1", "troll_01.ogg"), ("p1", "troll_02.ogg"), ("p1", "monster_03.ogg"), ("p2", "monster_18.ogg")],
    },
    "boss": {
        "hurt": [("p1", "roar_01.ogg"), ("p1", "roar_03.ogg"), ("p1", "monster_02.ogg"), ("p1", "monster_05.ogg"),
                 ("p2", "monster_11.ogg"), ("p2", "monster_15.ogg")],
        "death": [("p1", "monster_04.ogg"), ("p1", "monster_07.ogg"), ("p2", "monster_19.ogg"), ("p2", "monster_20.ogg")],
    },
}


def targets():
    """Yield (set, kind, n, pack, member) for every file this script places."""
    for s, kinds in SETS.items():
        for kind in ("hurt", "death"):
            for n, (pack, member) in enumerate(kinds[kind], start=1):
                yield s, kind, n, pack, member


def fetch_archive(pack: str) -> zipfile.ZipFile:
    url = FILES + PACKS[pack][0]
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as r:
        return zipfile.ZipFile(io.BytesIO(r.read()))


def find_member(z: zipfile.ZipFile, member: str):
    for info in z.infolist():
        if not info.is_dir() and os.path.basename(info.filename) == member and "__MACOSX" not in info.filename:
            return info
    return None


def write_clip(z: zipfile.ZipFile, info, dest: str) -> None:
    data = z.read(info)
    ext = os.path.splitext(info.filename)[1].lower()
    if ext == ".ogg":
        with open(dest, "wb") as f:
            f.write(data)
        return
    # .wav / .mp3 source: convert to Ogg Vorbis with ffmpeg via stdin, so nothing else touches disk
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("ffmpeg is needed to convert " + ext + " (install it: sudo apt install ffmpeg / brew install ffmpeg)")
    subprocess.run([ffmpeg, "-v", "error", "-y", "-f", ext[1:], "-i", "pipe:0", "-c:a", "libvorbis", "-q:a", "5", dest],
                   input=data, check=True)


def main() -> int:
    archives: dict = {}
    failed = 0
    fetched = 0
    for s, kind, n, pack, member in targets():
        folder = os.path.join(DEST, s)
        os.makedirs(folder, exist_ok=True)
        name = f"{kind}_{n}.ogg"
        dest = os.path.join(folder, name)
        if os.path.exists(dest) and os.path.getsize(dest) > 1000:
            print(f"have {s}/{name}")
            continue
        try:
            if pack not in archives:
                archives[pack] = None  # a failed download is not retried for every file
                archives[pack] = fetch_archive(pack)
            if archives[pack] is None:
                raise RuntimeError(f"{PACKS[pack][0]} could not be downloaded")
            info = find_member(archives[pack], member)
            if info is None:
                raise RuntimeError(f"{member} not in {PACKS[pack][0]}")
            write_clip(archives[pack], info, dest)
            fetched += 1
            print(f"fetched {s}/{name} ({os.path.getsize(dest) // 1024} KB) from {PACKS[pack][0]}:{member}")
        except Exception as e:  # noqa: BLE001
            failed += 1
            print(f"FAILED {s}/{name}: {e}")
            if os.path.exists(dest):
                os.remove(dest)
    if failed and not fetched and not any(os.listdir(os.path.join(DEST, s)) for s in SETS):
        print("FAILED: no CC0 grunt source could be reached; the game falls back to enemy_hurt / enemy_die")
        return 1

    readme = ["# Enemy grunts", "",
              "CC0 (" + CC0 + "); fetched by `scripts/fetch_grunts.py`. AudioManager plays each folder as",
              "`grunt_hurt_<set>` / `grunt_death_<set>`, pitched by `VoiceSets.PitchOffset` (small +0.25, medium 0,",
              "large -0.25, boss -0.4), so small and medium share clips.", ""]
    for key, (zipname, slug, title, author) in PACKS.items():
        readme.append(f"- {key}: \"{title}\" by {author}, https://opengameart.org/content/{slug} ({zipname})")
    readme.append("")
    for s in SETS:
        readme.append(f"## {s}")
        readme.append("")
        for s2, kind, n, pack, member in targets():
            if s2 == s:
                readme.append(f"- {kind}_{n}.ogg: {pack} {member}")
        readme.append("")
    with open(os.path.join(DEST, "README.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(readme))

    lic = os.path.join(ROOT, "assets", "LICENSES.md")
    with open(lic, encoding="utf-8") as f:
        text = f.read()
    wanted = ["## Enemy grunts (CC0)", "",
              "CC0 (" + CC0 + "); fetched by `scripts/fetch_grunts.py` into `assets/audio/grunts/` (per-file origin in its README.md).", ""]
    wanted += [f"- \"{title}\" by {author}, https://opengameart.org/content/{slug}" for _, slug, title, author in PACKS.values()]
    missing = [line for line in wanted if line and line not in text]
    if missing:
        block = wanted if "## Enemy grunts (CC0)" not in text else missing
        with open(lic, "a", encoding="utf-8") as f:
            f.write(("\n" if not text.endswith("\n\n") else "") + "\n".join(block) + "\n")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
