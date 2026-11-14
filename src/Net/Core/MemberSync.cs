using System.Text.Json;
using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

/// <summary>
/// When a member's sheet must go out again as a <see cref="PartySync"/>. It used to go out on every MemberChanged, and
/// MemberChanged fires on every swing, hit taken, dodge and parry, so a fight sent each member's whole CharacterDto (gear,
/// affixes and all, 1-2 KB of JSON) up to ten times a second to every client, while what those events change (health,
/// stamina, mana, cooldown) already travels in the binary Vitals at 10 Hz. Meanwhile an effect running out changed the
/// sheet with no MemberChanged at all, so clients kept showing an expired buff and its stats.
/// The rule now compares content: the sheet as JSON with the vitals and the effect timers left out. Anything else in the
/// DTO (gear, level, xp, points, skills, an effect starting, ending, stacking or changing strength, Mangled) differs, and
/// a field added to the DTO later is covered without touching this. Timers are left to the client to count down, and
/// sent again only when the server's clock for an effect drifts from that countdown (a refresh) by more than a second.
/// </summary>
public static class MemberSync
{
    /// <summary>How far an effect's remaining time may differ from the client's countdown before the sheet is resent.</summary>
    public const float TimerTolerance = 1f;

    /// <summary>The sheet's lasting content: <paramref name="dto"/> as JSON with hp, stamina, mana and effect timers zeroed (restored before returning).</summary>
    public static string Signature(CharacterDto dto)
    {
        var (hp, stamina, mana) = (dto.Hp, dto.Stamina, dto.Mana);
        var timers = dto.Effects.Select(e => e.Remaining).ToArray();
        dto.Hp = dto.Stamina = dto.Mana = 0;
        foreach (var e in dto.Effects) e.Remaining = 0;
        try { return JsonSerializer.Serialize(dto, NetCodec.Json); }
        finally
        {
            (dto.Hp, dto.Stamina, dto.Mana) = (hp, stamina, mana);
            for (var i = 0; i < timers.Length; i++) dto.Effects[i].Remaining = timers[i];
        }
    }

    /// <summary>
    /// True when an effect's remaining time now differs by more than <see cref="TimerTolerance"/> from what a client counting
    /// down since the last send shows (the effect was refreshed). Only meaningful when the signatures match, so the two
    /// lists hold the same effects in the same order.
    /// </summary>
    public static bool TimersDrifted(IReadOnlyList<StatusDto> sent, double sentAt, IReadOnlyList<StatusDto> now, double nowAt)
    {
        if (sent.Count != now.Count) return true;
        var elapsed = (float)(nowAt - sentAt);
        for (var i = 0; i < now.Count; i++)
        {
            var shown = Math.Max(0f, sent[i].Remaining - elapsed);
            if (Math.Abs(shown - now[i].Remaining) > TimerTolerance) return true;
        }
        return false;
    }
}
