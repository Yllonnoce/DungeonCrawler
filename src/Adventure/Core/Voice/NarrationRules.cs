namespace DungeonCrawler.Adventure.Voice;

/// <summary>
/// Which of a floor's narration entries are due now: the ones for this trigger (and room), whose flags allow them,
/// that have not been said yet this run. The said set rides in the save, so a reload or a second visit stays quiet,
/// while an entry held back by a flag stays unsaid and can still play on a later visit. Engine-free.
/// </summary>
public static class NarrationRules
{
    private const string Prefix = "narr:";

    /// <summary>The key an entry is remembered under: floor, trigger, room and the text, so it survives a save and never collides between floors.</summary>
    public static string Key(int floor, NarrationPlan n)
        => $"{Prefix}{floor}:{n.Trigger}:{n.Room?.ToString() ?? "-"}:{n.Line.Text.Trim().ToLowerInvariant()}";

    /// <summary>The entries to play for a trigger (for <see cref="NarrationTrigger.RoomEnter"/>, that room), in plan order.</summary>
    public static IReadOnlyList<NarrationPlan> Due(FloorPlan plan, NarrationTrigger trigger, int? room, IReadOnlyCollection<string> flags, IReadOnlySet<string> said, int floor)
        => plan.Narration
            .Where(n => n.Trigger == trigger
                && (trigger != NarrationTrigger.RoomEnter || (room != null && n.Room == room))
                && n.Line.Text.Trim().Length > 0
                && FlagRules.Allowed(n.RequiresFlag, n.ForbidsFlag, flags)
                && !said.Contains(Key(floor, n)))
            .ToList();

    /// <summary>Whether a boss is the campaign's antagonist (the same name): then it speaks in the villain's voice, not the stock boss's.</summary>
    public static bool BossIsAntagonist(string bossName, string antagonistName)
        => bossName.Trim().Length > 0 && string.Equals(bossName.Trim(), antagonistName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The estimate's constants, fitted to the shipped bank (2026-11-12: 198 narration, taunt and boss recordings, 0.076 s a
    /// character at the median, 0.096 at the 95th percentile, the longest 18.3 s): 1.5 s + 0.085 s a character, at most 20 s,
    /// covers 97.5% of them with a median of 2.6 s to spare and about 0.6 s on a short line.
    /// </summary>
    public const double SpeakFloor = 1.5, SpeakPerChar = 0.085, SpeakCap = 20.0;

    /// <summary>Roughly how long a line takes to be heard when its recording is not to hand: 1.5 s, 0.085 s a character, capped at twenty.</summary>
    public static double SpeakSeconds(string text) => Math.Min(SpeakCap, SpeakFloor + SpeakPerChar * (text?.Trim().Length ?? 0));

    /// <summary>A pause after a recording before the next line; the least and the most a recording holds the channel.</summary>
    public const double Breath = 0.3, HeardFloor = 1.0, HeardCap = 30.0;

    /// <summary>
    /// How long a line holds the voice channel: its recording's real length and a breath when the recording is there
    /// (<paramref name="recordedSeconds"/>), else the estimate. What waits for a line waits this long.
    /// </summary>
    public static double HeardSeconds(string text, double? recordedSeconds)
        => recordedSeconds is { } r && r > 0 ? Math.Clamp(r + Breath, HeardFloor, HeardCap) : SpeakSeconds(text);

    /// <summary>
    /// A floor's arrival: the parting taunt of the floor left (any that spoke, the floor's own or the campaign's), then the
    /// tier's entry line when there is one, then the floor's opening, each once the line before it has been heard.
    /// </summary>
    public static (double? TierAt, double OpenAt) Arrival(double now, double? partingSeconds, double? tierSeconds)
    {
        var after = now + (partingSeconds ?? 0);
        return tierSeconds is { } tier ? (after, after + tier) : (null, after);
    }

    /// <summary>
    /// The tier whose entry line opens this floor: the first floor of each tier of the campaign proper (1, 6, 11, 16),
    /// when the campaign wrote that tier a line. Never the tavern, never an endless floor past the last.
    /// </summary>
    public static TierPlan? TierEntry(CampaignPlan campaign, int floor)
    {
        if (floor < 1 || floor > Dungeon.TierRules.FinalFloor || Dungeon.TierRules.FloorInTier(floor) != 1) return null;
        var tier = campaign.TierFor(Dungeon.TierRules.TierIndex(floor));
        return tier != null && tier.EnterLine.Text.Trim().Length > 0 ? tier : null;
    }

    /// <summary>The key a tier's entry line is remembered under in the said record (kept forever, like a narration entry's).</summary>
    public static string TierKey(TierPlan tier) => $"{Prefix}tier:{tier.Index}:{tier.EnterLine.Text.Trim().ToLowerInvariant()}";

    /// <summary>
    /// The longest the victory recap waits once the closing narration has all been raised, so it can never hang on an
    /// estimate. (Counted from the drain, not the death: the death line and two closing lines already take about 35 s.)
    /// </summary>
    public const double RecapCapSeconds = 30;

    /// <summary>Whether the last boss's death queued narration the victory recap should wait for; with none it shows at once.</summary>
    public static bool HoldsRecap(int queuedAtDeath) => queuedAtDeath > 0;

    /// <summary>
    /// A held victory recap shows once the queue has drained and its last line has been heard, or
    /// <see cref="RecapCapSeconds"/> after the queue drained (<paramref name="drainedAt"/>, null while entries wait).
    /// </summary>
    public static bool RecapDue(double now, double? drainedAt, int queued, double speakingUntil)
        => queued == 0 && (now >= speakingUntil || (drainedAt is { } d && now - d >= RecapCapSeconds));
}

/// <summary>
/// The one voice channel the narrator, the villain and the bosses share: whatever was raised last holds it for its
/// <see cref="NarrationRules.HeardSeconds"/>, and a queued narration entry waits until it is free. Engine-free.
/// </summary>
public sealed class SpeechClock
{
    /// <summary>When the line now being said will have been heard.</summary>
    public double Until { get; private set; } = double.NegativeInfinity;

    /// <summary>A line was raised now: the channel is held until it has been heard (a shorter line never cuts the wait).</summary>
    public void Spoke(double now, string text) => Spoke(now, seconds: NarrationRules.SpeakSeconds(text));

    /// <summary>A line was raised now that is heard for <paramref name="seconds"/> (its recording's length, see <see cref="NarrationRules.HeardSeconds"/>).</summary>
    public void Spoke(double now, double seconds) => Until = Math.Max(Until, now + seconds);

    public bool Free(double now) => now >= Until;

    /// <summary>A floor change, a load or a wipe: what was being said no longer holds anything back.</summary>
    public void Reset() => Until = double.NegativeInfinity;
}
