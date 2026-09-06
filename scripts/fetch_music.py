#!/usr/bin/env python3
"""Download the CC0 music tracks (OpenGameArt) into assets/audio/music and log them in assets/LICENSES.md.
Run once, then `godot --headless --import --path .` so Godot imports the files."""
import os, sys, urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEST = os.path.join(ROOT, "assets", "audio", "music")
UA = {"User-Agent": "Mozilla/5.0 (DungeonCrawler asset fetch)"}
FILES = "https://opengameart.org/sites/default/files/"
# (file name here, source file, page slug, title, author)
TRACKS = [
    ("menu_old_tower_inn.mp3", "The_Old_Tower_Inn.mp3", "medieval-the-old-tower-inn", "Medieval: The Old Tower Inn", "RandomMind"),
    ("crypt_dungeon_theme.mp3", "dungeon.mp3", "dungeon-theme-0", "Dungeon theme", "Katana"),
    ("crypt_dark_theme.ogg", "theme_0.ogg", "dark-theme", "Dark Theme", "JaggedStone"),
    ("crypt_medieval_exploration.mp3", "Exploration_0.mp3", "medieval-exploration", "Medieval: Exploration", "RandomMind"),
    ("caves_dark_cavern_1.ogg", "dark_cavern_ambient_001.ogg", "dark-cavern-ambient", "Dark Cavern Ambient (1)", "Paul Wortmann"),
    ("caves_dark_cavern_2.ogg", "dark_cavern_ambient_002.ogg", "dark-cavern-ambient", "Dark Cavern Ambient (2)", "Paul Wortmann"),
    ("caves_going_deep.ogg", "S31-Going%20Deep.ogg", "going-deep", "Going Deep", "section31"),
    ("forge_heavy_dungeon.ogg", "heavy_dungeon_bpm160_0.ogg", "heavy-dungeon", "Heavy Dungeon", "MintoDog"),
    ("forge_cursed_tower_veranda.ogg", "cursed_tower_veranda_loop.ogg", "cursed-tower-veranda", "Cursed Tower Veranda (loop)", "Bobjt"),
    ("forge_ghosts_and_heroes.ogg", "ghosts_n_heroes_2025_loop.ogg", "ghosts-heroes", "Ghosts & Heroes (2025 loop)", "Bobjt"),
    ("abyss_dark_shrine_loop.ogg", "qubodup-yd-DarkShrineLoop-OpenGameArt.ogg", "dark-shrine-loop", "Dark Shrine Loop", "qubodup"),
    ("abyss_dungeon_ambience.ogg", "dungeon002_0.ogg", "dungeon-ambience", "Dungeon Ambience", "yd"),
    ("abyss_dark_intro.ogg", "Dark%20Intro_0.ogg", "dark-intro", "Dark Intro", "Nikke"),
    ("combat_battle_theme_a.mp3", "battleThemeA.mp3", "battle-theme-a", "Battle Theme A", "cynicmusic"),
    ("combat_battle_theme_b.mp3", "battleThemeB.mp3", "battle-theme-b-for-rpg", "Battle Theme B for RPG", "cynicmusic"),
    ("combat_medieval_battle.mp3", "battle_8.mp3", "medieval-battle", "Medieval: Battle", "RandomMind"),
    ("boss_battle_rpg_theme.ogg", "CleytonRX%20-%20Battle%20RPG%20Theme%20Var_0.ogg", "boss-battle-theme", "Boss Battle Theme (variation)", "CleytonKauffman"),
    ("sting_victory.mp3", "victory_0.mp3", "medieval-victory-theme", "Medieval: Victory Theme", "RandomMind"),
    ("sting_defeat.mp3", "defeat_0.mp3", "medieval-defeat-theme", "Medieval: Defeat Theme", "RandomMind"),
]


def main() -> int:
    os.makedirs(DEST, exist_ok=True)
    failed = 0
    for name, src, slug, title, author in TRACKS:
        dest = os.path.join(DEST, name)
        if os.path.exists(dest) and os.path.getsize(dest) > 1000:
            print(f"have {name}")
            continue
        url = FILES + src
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=180) as r, open(dest, "wb") as f:
                f.write(r.read())
            print(f"fetched {name} ({os.path.getsize(dest) // 1024} KB)")
        except Exception as e:  # noqa: BLE001
            failed += 1
            print(f"FAILED {name}: {e}")
            if os.path.exists(dest):
                os.remove(dest)
    lic = os.path.join(ROOT, "assets", "LICENSES.md")
    with open(lic, encoding="utf-8") as f:
        text = f.read()
    if "## Music (OpenGameArt, CC0)" not in text:
        lines = ["", "## Music (OpenGameArt, CC0)", "", "All tracks are CC0 (https://creativecommons.org/publicdomain/zero/1.0/); fetched by `scripts/fetch_music.py` into `assets/audio/music/`.", ""]
        for name, _, slug, title, author in TRACKS:
            lines.append(f"- {name}: \"{title}\" by {author}, https://opengameart.org/content/{slug}")
        with open(lic, "a", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
