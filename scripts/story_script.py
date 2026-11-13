#!/usr/bin/env python3
"""Print the shipped campaign as a readable Markdown script.

Read-only: reads data/adventures/builtin/*.json and writes nothing under data/.

Usage: python3 scripts/story_script.py [--act N] [--out FILE]
  --act 1..4  limit to floors 1-5, 6-10, 11-15, 16-20
  --out FILE  write to FILE instead of stdout
"""
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "data" / "adventures" / "builtin"

# The objective each shipped floor really generates on the shipped seed, which is not always the frame's (floor 9's
# frame says shiftingvault and it generates none; floors 11 and 13 say none and generate shiftingvault and jailbreak).
# Read on 2026-11-12 from `godot --headless --path . -- --builtin --floor=N --no-ai --quit-after 20` for N = 1..20
# (the "objective=" field of the "[DungeonBuilder] floor N" line). Rerun that if the seed or the generator changes.
GENERATED = {2: "jailbreak", 4: "toxic", 7: "ritual", 11: "shiftingvault", 12: "jailbreak", 13: "jailbreak",
             14: "shiftingvault", 17: "toxic", 19: "ritual"}

# The objective a trigger belongs to (TauntTriggers.KindOf)
KIND_OF = {"Capture": "jailbreak", "Freed": "jailbreak", "AllFreed": "jailbreak", "GasThickens": "toxic", "GasLethal": "toxic",
           "WrongLever": "shiftingvault", "GateShift": "shiftingvault", "VaultOpened": "shiftingvault",
           "RitualStart": "ritual", "RitualistDown": "ritual", "WaveBreak": "ritual", "RitualDone": "ritual"}

# Why a line in the data is never heard, as the engine plays it (AdventureDirector, Main)
NOT_INTRO = "not played: the floor opens with its FloorEnter taunt instead"
NOT_QUEST = "not played: quest lines are not spoken, the dialogue nodes carry the scene"
NOT_RITUALIST = "not played: the rite's figure, whom the party defends, cannot be talked to"


def load(name):
    with open(DATA / name, encoding="utf-8") as fh:
        return json.load(fh)


def text_of(line):
    """The text of a {"Text": ...} line object, or None when empty/missing."""
    if isinstance(line, dict):
        t = (line.get("Text") or "").strip()
        return t or None
    return None


def say(out, speaker, line, indent="", note="", unplayed=None):
    t = text_of(line)
    if t:
        note = f"{note}; {unplayed}" if note and unplayed else (unplayed or note)
        tag = f" _({note})_" if note else ""
        out.append(f"{indent}**{speaker}:**{tag} {t}")
        out.append("")


def gate_of(entry):
    """A narration entry's or taunt's flag gate, for the note: "needs f", "not if g"."""
    bits = []
    if entry.get("RequiresFlag"):
        bits.append(f"needs {entry['RequiresFlag']}")
    if entry.get("ForbidsFlag"):
        bits.append(f"not if {entry['ForbidsFlag']}")
    return "".join(f", {b}" for b in bits)


def node_order(script):
    """Nodes in script order: the start node first, then the rest as listed."""
    nodes = script.get("Nodes") or []
    start = script.get("Start")
    return sorted(nodes, key=lambda n: 0 if n.get("Id") == start else 1)


def emit_script(out, speaker, script, unplayed=None):
    if not script:
        return
    for node in node_order(script):
        say(out, speaker, node.get("Line"), note=f"node {node.get('Id')}", unplayed=unplayed)
        aside = node.get("Aside")
        if aside:
            say(out, aside.get("Member", "Party"), aside.get("Line"), indent="  ", note="aside", unplayed=unplayed)
        for ch in node.get("Choices") or []:
            bits = []
            if ch.get("Effect") and ch["Effect"] != "None":
                bits.append(ch["Effect"])
            if ch.get("Flag"):
                bits.append(f"sets {ch['Flag']}")
            if ch.get("RequiresFlag"):
                bits.append(f"needs {ch['RequiresFlag']}")
            if ch.get("ForbidsFlag"):
                bits.append(f"not if {ch['ForbidsFlag']}")
            if ch.get("Next"):
                bits.append(f"to {ch['Next']}")
            extra = f" _[{'; '.join(bits)}]_" if bits else ""
            out.append(f"  - **Party (choice):** {ch.get('Text', '')}{extra}")
        out.append("")


def emit_floor(out, camp, floor_no):
    fl = camp["Floors"][floor_no - 1]
    f = load(f"floor_{floor_no:02d}.json")
    out.append(f"## Floor {floor_no}: {fl.get('Title') or f.get('Title')}")
    out.append("")
    if fl.get("Mood"):
        out.append(f"_Mood: {fl['Mood']}_")
        out.append("")
    generated = GENERATED.get(floor_no)
    if fl.get("Objective") or generated:
        frame = fl.get("Objective") or "none"
        out.append(f"_Objective: {frame} in the frame; the shipped seed generates {generated or 'none'}_")
        out.append("")
    if fl.get("Story"):
        out.append(f"**Story beat:** {fl['Story']}")
        out.append("")

    tier = camp["Tiers"][(floor_no - 1) // 5]
    if (floor_no - 1) % 5 == 0:
        say(out, "Narrator", tier.get("EnterLine"), note=f"tier {tier['Index'] + 1} entry")

    say(out, "Narrator", f.get("IntroLine"), note="floor intro", unplayed=NOT_INTRO if generated else None)

    for n in f.get("Narration") or []:
        where = n.get("Trigger", "")
        if n.get("Room") is not None:
            where += f" room {n['Room']}"
        say(out, "Narrator", n.get("Line"), note=where + gate_of(n))

    quests = {q["Id"]: q for q in f.get("Quests") or []}
    for npc in f.get("Npcs") or []:
        gate = []
        if npc.get("RequiresFlag"):
            gate.append(f"needs {npc['RequiresFlag']}")
        if npc.get("ForbidsFlag"):
            gate.append(f"not if {npc['ForbidsFlag']}")
        ritualist = npc.get("Ritualist") and generated == "ritual"
        out.append(f"### {npc['Name']}" + (f" _({'; '.join(gate)})_" if gate else ""))
        out.append("")
        emit_script(out, npc["Name"], npc.get("Script"), unplayed=NOT_RITUALIST if ritualist else None)
        bet = npc.get("Betrayal")
        if bet:
            for k, v in bet.items():
                if isinstance(v, dict) and text_of(v):
                    say(out, npc["Name"], v, note=f"betrayal {k}")
        q = quests.get(npc.get("QuestId"))
        if q:
            out.append(f"_Quest: {q.get('Title')} ({q.get('Template')}): {q.get('Objective')}_")
            out.append("")
            for key in ("Offer", "Accept", "Decline", "Complete", "Fail"):
                say(out, npc["Name"], (q.get("Lines") or {}).get(key), note=f"quest {key.lower()}", unplayed=NOT_RITUALIST if ritualist else quest_unplayed(q, key))
    npc_quests = {n.get("QuestId") for n in f.get("Npcs") or []}
    for q in quests.values():
        if q["Id"] in npc_quests:
            continue
        out.append(f"### Quest: {q.get('Title')}")
        out.append("")
        who = q.get("GiverNpc") or "Narrator"
        for key in ("Offer", "Accept", "Decline", "Complete", "Fail"):
            say(out, who, (q.get("Lines") or {}).get(key), note=f"quest {key.lower()}", unplayed=quest_unplayed(q, key))

    taunts = f.get("Taunts") or []
    if taunts:
        villain = camp["Antagonist"]["Name"]
        out.append("### Floor taunts")
        out.append("")
        for t in taunts:
            note = t.get("Trigger", "")
            if t.get("Objective"):
                note += f", {t['Objective']}"
            say(out, villain, t.get("Line"), note=note + gate_of(t), unplayed=floor_taunt_unplayed(t, floor_no, generated))

    hordes = [h for h in f.get("Hordes") or [] if text_of(h.get("Line"))]
    if hordes:
        out.append("### Hordes")
        out.append("")
        for h in hordes:
            say(out, "Narrator", h.get("Line"), note=f"{h.get('Name')}, {h.get('Trigger')}")

    boss = f.get("Boss")
    if boss:
        out.append(f"### Boss: {boss.get('Name')}")
        out.append("")
        if not any(text_of(boss.get(k)) for k in ("IntroLine", "HalfLine", "AltHalfLine", "DeathLine", "AltDeathLine")):
            out.append("_No lines of its own: the narrator's BossSeen and BossDead lines speak for it._")
            out.append("")
        for key in ("IntroLine", "HalfLine", "AltHalfLine", "DeathLine", "AltDeathLine"):
            note = key.replace("Line", "").lower()
            if key.startswith("Alt") and boss.get("AltDeathFlag"):
                note += f", if {boss['AltDeathFlag']}"
            say(out, boss.get("Name", "Boss"), boss.get(key), note=note)


def quest_unplayed(q, key):
    """Quest lines: only an escort's Complete is shown (a subtitle) and a Fail goes to the message log."""
    if key == "Complete" and q.get("Template") == "Escort":
        return "shown as a subtitle when the escort arrives, not voiced"
    if key == "Fail":
        return "shown in the message log if the quest fails, not voiced"
    return NOT_QUEST


def floor_taunt_unplayed(t, floor_no, generated):
    """A floor taunt nothing can raise on the shipped seed: a FloorEnter on a floor with no objective, a line for another objective, or the last floor's Descend."""
    trig = t.get("Trigger")
    if t.get("Objective") and t["Objective"] != generated:
        return f"not played: the floor generates {generated or 'no objective'}"
    if KIND_OF.get(trig) and KIND_OF[trig] != generated:
        return f"not played: a {KIND_OF[trig]} moment, and the floor generates {generated or 'no objective'}"
    if trig == "Idle" and not generated:
        return "not played: the dawdle taunt is for objective floors"
    if trig == "FloorEnter" and not generated:
        return "not played: only an objective floor opens with a taunt"
    if trig == "Descend" and floor_no == 20:
        return "not played in the story: it would only be heard going on past floor 20 into the endless depths"
    return None


def emit_campaign_tail(out, camp):
    out.append("## Campaign lines")
    out.append("")
    villain = camp["Antagonist"]["Name"]
    out.append(f"### Campaign taunts ({villain})")
    out.append("")
    for t in camp.get("Taunts") or []:
        note = t.get("Trigger", "") + gate_of(t)
        if t.get("Objective"):
            note += f", {t['Objective']}"
        unplayed = "not heard on floors 1-20: every objective floor there opens with its own line" if t.get("Trigger") == "FloorEnter" else None
        say(out, villain, t.get("Line"), note=note, unplayed=unplayed)

    m = camp.get("Merchant") or {}
    merchant = next((c["Name"] for c in camp.get("Cast", []) if c.get("Merchant")), "Merchant")
    out.append(f"### Merchant ({merchant})")
    out.append("")
    emit_script(out, merchant, m.get("Briefing"))
    for fl in m.get("FloorLines") or []:
        for key, v in fl.items():
            if isinstance(v, dict):
                say(out, merchant, v, note=f"floor {fl.get('Floor')} {key.lower()}")
    for g in m.get("Gathering") or []:
        say(out, merchant, g, note="gathering")

    out.append("### Banter")
    out.append("")
    for b in camp.get("Banter") or []:
        rng = f"floors {b.get('MinFloor')}+" if b.get("MinFloor") else ""
        out.append(f"_{b.get('Id')} {rng}_".rstrip())
        out.append("")
        for ln in b.get("Lines") or []:
            say(out, ln.get("Member", "Party"), ln.get("Line"), note=ln.get("Line", {}).get("Delivery") or "")

    out.append("### Barks")
    out.append("")
    for b in camp.get("Barks") or []:
        for ln in b.get("Lines") or []:
            say(out, b.get("Member", "Party"), ln.get("Line"), note=ln.get("Trigger", ""))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--act", type=int, choices=[1, 2, 3, 4])
    ap.add_argument("--out")
    args = ap.parse_args()

    camp = load("campaign.json")
    first, last = (1, 20) if not args.act else ((args.act - 1) * 5 + 1, args.act * 5)

    out = [f"# {camp['Title']}", ""]
    if not args.act:
        out += [camp.get("Synopsis", ""), ""]
    else:
        out += [f"_Act {args.act}: floors {first}-{last}_", ""]
    for n in range(first, last + 1):
        emit_floor(out, camp, n)
    if not args.act:
        emit_campaign_tail(out, camp)

    body = "\n".join(out).rstrip() + "\n"
    if args.out:
        p = Path(args.out)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(body, encoding="utf-8")
    else:
        sys.stdout.write(body)


if __name__ == "__main__":
    main()
