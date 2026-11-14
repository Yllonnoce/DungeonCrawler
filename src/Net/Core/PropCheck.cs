using System.Globalization;
using System.Text;
using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

/// <summary>
/// The lasting state of a floor's props, as the prop check sends it (<see cref="PropStateMsg"/>): every list is of the
/// props' own indices (chest SpawnIndex, door and lever VaultIndex, secret Index, trap SpawnIndex, barrel SpawnIndex).
/// Hazard phases are not here (HazardPhaseMsg carries them), nor plaques (they have no state).
/// </summary>
public sealed class PropStateDto
{
    public List<int> OpenedChests { get; set; } = new();
    /// <summary>Mimics sprung and not yet beaten (an opened chest is only in <see cref="OpenedChests"/>).</summary>
    public List<int> SprungChests { get; set; } = new();
    /// <summary>Jammed locks on chests still shut.</summary>
    public List<int> JammedChests { get; set; } = new();
    /// <summary>Every open door: vault doors, the safe room's, objective gates and the jail.</summary>
    public List<int> OpenDoors { get; set; } = new();
    /// <summary>Doors someone has made out in the wall (open or not).</summary>
    public List<int> RevealedDoors { get; set; } = new();
    public List<int> PulledLevers { get; set; } = new();
    public List<int> RevealedSecrets { get; set; } = new();
    public List<int> OpenSecrets { get; set; } = new();
    /// <summary>The floor's own traps (not the party's).</summary>
    public List<int> RevealedTraps { get; set; } = new();
    public List<int> DisarmedTraps { get; set; } = new();
    public List<int> TriggeredTraps { get; set; } = new();
    /// <summary>Barrels and crates still standing.</summary>
    public List<int> Barrels { get; set; } = new();
    /// <summary>The party's traps still armed, with their ids (<see cref="PropCheck.PlacedTrapBase"/> up).</summary>
    public List<PlacedTrapDto> PlacedTraps { get; set; } = new();
    public bool ShrineUsed { get; set; }
}

/// <summary>
/// The prop check's fingerprint (the floor's props, next to the item check's items and the state check's bag and party).
/// The server hashes the props it holds, a client hashes its own the same way, and a mismatch asks for the whole
/// <see cref="PropStateDto"/>, which the client applies silently in either direction it can. FNV-1a over an invariant
/// string, as <see cref="StateCheck"/>.
/// </summary>
public static class PropCheck
{
    /// <summary>Traps the party sets get ids from here up: clear of the floor's own (0..) and the plan's extras (1000..).</summary>
    public const int PlacedTrapBase = 2000;

    /// <summary>The next placed trap's id: one past the highest in use on the floor (ids only need to be unique among the traps standing).</summary>
    public static int NextPlacedTrapId(IEnumerable<int> inUse)
    {
        var max = PlacedTrapBase - 1;
        foreach (var id in inUse) if (id > max) max = id;
        return max + 1;
    }

    public static string Signature(PropStateDto p)
    {
        var sb = new StringBuilder();
        void List(char tag, IEnumerable<int> ids)
        {
            sb.Append(tag);
            foreach (var i in ids.Distinct().OrderBy(i => i)) sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append('|');
        }
        List('c', p.OpenedChests); List('m', p.SprungChests); List('j', p.JammedChests);
        List('d', p.OpenDoors); List('r', p.RevealedDoors); List('l', p.PulledLevers);
        List('s', p.RevealedSecrets); List('o', p.OpenSecrets);
        List('t', p.RevealedTraps); List('x', p.DisarmedTraps); List('g', p.TriggeredTraps);
        List('b', p.Barrels);
        sb.Append('p');
        foreach (var t in p.PlacedTraps.OrderBy(t => t.Id)) sb.Append(t.Id.ToString(CultureInfo.InvariantCulture)).Append(':').Append(t.Kind).Append(',');
        sb.Append('|').Append(p.ShrineUsed ? 'U' : '-');
        return sb.ToString();
    }

    public static uint Hash(PropStateDto p) => StateCheck.Fnv(Signature(p));

    /// <summary>What differs between two prop states, for the log: "doors open +3 -1" (in <paramref name="server"/> only / in <paramref name="mine"/> only).</summary>
    public static List<string> Differences(PropStateDto mine, PropStateDto server)
    {
        var diffs = new List<string>();
        void Compare(string name, IEnumerable<int> a, IEnumerable<int> b)
        {
            var more = b.Except(a).OrderBy(i => i).ToList();
            var less = a.Except(b).OrderBy(i => i).ToList();
            if (more.Count + less.Count == 0) return;
            diffs.Add($"{name}{string.Concat(more.Select(i => $" +{i}"))}{string.Concat(less.Select(i => $" -{i}"))}");
        }
        Compare("chests opened", mine.OpenedChests, server.OpenedChests); Compare("chests sprung", mine.SprungChests, server.SprungChests);
        Compare("chests jammed", mine.JammedChests, server.JammedChests); Compare("doors open", mine.OpenDoors, server.OpenDoors);
        Compare("doors seen", mine.RevealedDoors, server.RevealedDoors); Compare("levers", mine.PulledLevers, server.PulledLevers);
        Compare("secrets seen", mine.RevealedSecrets, server.RevealedSecrets); Compare("secrets open", mine.OpenSecrets, server.OpenSecrets);
        Compare("traps seen", mine.RevealedTraps, server.RevealedTraps); Compare("traps disarmed", mine.DisarmedTraps, server.DisarmedTraps);
        Compare("traps sprung", mine.TriggeredTraps, server.TriggeredTraps); Compare("barrels", mine.Barrels, server.Barrels);
        Compare("party traps", mine.PlacedTraps.Select(t => t.Id), server.PlacedTraps.Select(t => t.Id));
        if (mine.ShrineUsed != server.ShrineUsed) diffs.Add($"shrine {(server.ShrineUsed ? "used" : "fresh")} on the server");
        return diffs;
    }

    /// <summary>
    /// Whether a client asks for the props again: its hash differs, and it has not already asked for this very server
    /// state lately. A difference the apply cannot mend (a barrel the client lost that the server still has) then costs
    /// one ask per change of the server's props, or one every <paramref name="retrySeconds"/>, instead of one every check.
    /// </summary>
    public static bool ShouldAsk(uint mine, uint server, uint? askedFor, double askedAt, double now, double retrySeconds = 20)
        => mine != server && (askedFor != server || now - askedAt >= retrySeconds);
}
