using DungeonCrawler.Adventure.Voice;
using DungeonCrawler.Save;

namespace DungeonCrawler.Adventure.Run;

/// <summary>The floor card on a descent: the floor's name, time, kills, elites, secrets found/total, downs, gold found, the worst moment and the party's reaction line.</summary>
public sealed record FloorCardModel(string Title, string Time, int Kills, int Elites, string Secrets, int Downs, int GoldFound, string WorstMoment, string Reaction);

/// <summary>One hero's line on the recap.</summary>
public sealed record HeroLine(string Name, int Kills, float Damage, int Downs, int Rescues, float PossessSeconds, string Text);

/// <summary>The run recap on victory and on a Death Clause end: floor by floor, per hero, the elites, boons, clauses, deepest floor, score, and in co-op a role line per player.</summary>
public sealed record RunRecapModel(string Headline, List<string> FloorLines, List<HeroLine> Heroes, int ElitesSlain, List<string> Boons, List<string> Clauses, int DeepestFloor, int Score, List<string> PlayerLines, bool Victory);

/// <summary>Builds the cards from the ledger. Engine-free; the server builds and sends them, a client only shows them.</summary>
public static class RecapBuilder
{
    public static FloorCardModel FloorCard(FloorRecord r, string title, string reaction)
        => new(title, SlotInfo.FormatTime(r.Seconds), r.Kills, r.Elites, $"{r.SecretsFound}/{r.SecretsTotal}", r.Downs, r.GoldFound,
            r.WorstMoment != "" ? r.WorstMoment : LedgerRules.WorstMoment(r), reaction);

    /// <summary>The whole run: every floor's line, the heroes summed over the floors, the score, and the players' role lines when <paramref name="players"/> is given (never a damage rank).</summary>
    public static RunRecapModel Build(CampaignMemoryDto memory, IReadOnlyList<string> heroes, IReadOnlyList<string> clauses, IReadOnlyList<string> boons, bool victory, string headline, int wipes, IReadOnlyList<string>? players = null)
    {
        var floors = memory.Floors.Where(f => f.Floor >= 1).OrderBy(f => f.Floor).ToList();
        var floorLines = floors.Select(f => $"Floor {f.Floor}{(f.Title != "" ? " " + f.Title : "")}: {f.Kills} kills, {f.Downs} downs, {SlotInfo.FormatTime(f.Seconds)}{(f.Wipes > 0 ? ", wiped" : "")}").ToList();
        var heroLines = heroes.Select(name =>
        {
            var kills = floors.Sum(f => f.KillsBy.GetValueOrDefault(name));
            var damage = floors.Sum(f => f.DamageBy.GetValueOrDefault(name));
            var downs = floors.Sum(f => f.DownsBy.GetValueOrDefault(name));
            var rescues = floors.Sum(f => f.RescuesBy.GetValueOrDefault(name));
            var possess = floors.Sum(f => f.PossessSeconds.GetValueOrDefault(name));
            return new HeroLine(name, kills, damage, downs, rescues, possess, $"{name}: {kills} kills, {damage:0} dealt, down {downs}×, {rescues} rescues, {(int)Math.Round(possess / 60f)} min in the lead");
        }).ToList();
        var elites = floors.Sum(f => f.Elites);
        var secrets = floors.Sum(f => f.SecretsFound);
        var deepest = floors.Count == 0 ? 0 : floors.Max(f => f.Floor);
        var score = ScoreRules.Score(deepest, new ClauseSet(clauses).Heat, elites, secrets, wipes);
        var playerLines = players == null ? new List<string>() : RecapRules.RoleLines(floors, players).Select(r => $"{r.Role}: {r.Player}").ToList();
        return new RunRecapModel(headline, floorLines, heroLines, elites, boons.ToList(), clauses.ToList(), deepest, score, playerLines, victory);
    }

    /// <summary>"The Verger is slain. Absalom Quill's hold on the depths is broken." — the campaign's own names, with stock words when it has none.</summary>
    public static string VictoryHeadline(string? bossName, string? antagonist)
    {
        var boss = string.IsNullOrWhiteSpace(bossName) ? "The guardian" : bossName.Trim();
        var hold = string.IsNullOrWhiteSpace(antagonist) ? "The depths lie open." : $"{antagonist.Trim()}'s hold on the depths is broken.";
        return $"{boss} is slain. {hold}";
    }

    /// <summary>The lead's Travel line as the card's reaction ("Brand: \"...\""), chosen like a bark but never marked said; "" when the set has none.</summary>
    public static string Reaction(BarkSet? set, IReadOnlyDictionary<string, int> said, int floor, Random rng)
    {
        if (set == null) return "";
        var lines = set.Lines.Where(l => l.Trigger == BarkTrigger.Travel && l.Line.Text != "").Select(l => l.Line.Text).ToList();
        if (lines.Count == 0) return "";
        var text = BarkRepeat.ChooseOrOldest(said, lines, floor, rng);
        return text == null ? "" : $"{set.Member}: \"{text}\"";
    }
}
