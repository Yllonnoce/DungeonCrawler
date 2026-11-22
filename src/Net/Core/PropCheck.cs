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
    /// <summary>A railed floor's props (null off railed floors, so their hashes are as before).</summary>
    public RailPropsDto? Rail { get; set; }
}

/// <summary>
/// The lasting state of a railed floor's props: stop indices for the gates, spares and cage wagons, set-piece indices (a
/// plan's index in <c>FloorContents.RailSetPieces</c>) for the rest. A switch's or spur's "taken" (the cart, or the golem,
/// passed its points this ride) is in <see cref="TakenSwitches"/> for both kinds.
/// </summary>
public sealed class RailPropsDto
{
    public List<int> GatesUp { get; set; } = new();
    /// <summary>
    /// Track switches whose lever is thrown: a switch_out, a live switch_back (plan 11: a player's lever, thrown or straight as it was left), and
    /// a fixed switch_back (always thrown).
    /// </summary>
    public List<int> ThrownSwitches { get; set; } = new();
    /// <summary>Levers (switches and spurs) whose points are behind the cart this ride.</summary>
    public List<int> TakenSwitches { get; set; } = new();
    public List<int> ThrownSpurs { get; set; } = new();
    public List<int> ClearedHeaps { get; set; } = new();
    /// <summary>Pours whose quench valve is broken.</summary>
    public List<int> BrokenValves { get; set; } = new();
    public List<int> CollapsedTrestles { get; set; } = new();
    public List<int> SmashedBuffers { get; set; } = new();
    /// <summary>Barricades cut or smashed down (plan 11).</summary>
    public List<int> DownBarricades { get; set; } = new();
    public List<int> SparesUsed { get; set; } = new();
    public List<int> OpenCages { get; set; } = new();
    public bool KeysTaken { get; set; }
    /// <summary>The party-lane cells of ride 4's collapsing bridge gone now (plan 13; 0 while it stands or with none).</summary>
    public int BridgeGone { get; set; }
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
        if (p.Rail is { } r) // appended only on a railed floor: every other floor's hash is as it was
        {
            sb.Append("|R");
            List('G', r.GatesUp); List('S', r.ThrownSwitches); List('T', r.TakenSwitches); List('P', r.ThrownSpurs);
            List('H', r.ClearedHeaps); List('V', r.BrokenValves); List('E', r.CollapsedTrestles); List('B', r.SmashedBuffers); List('D', r.DownBarricades);
            List('C', r.SparesUsed); List('W', r.OpenCages);
            sb.Append(r.KeysTaken ? 'K' : '-');
            sb.Append("|N").Append(r.BridgeGone.ToString(CultureInfo.InvariantCulture));
        }
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
        if (mine.Rail != null || server.Rail != null)
        {
            RailPropsDto a = mine.Rail ?? new(), b = server.Rail ?? new();
            Compare("gates up", a.GatesUp, b.GatesUp); Compare("switches thrown", a.ThrownSwitches, b.ThrownSwitches);
            Compare("switches taken", a.TakenSwitches, b.TakenSwitches); Compare("spurs thrown", a.ThrownSpurs, b.ThrownSpurs);
            Compare("heaps cleared", a.ClearedHeaps, b.ClearedHeaps); Compare("valves broken", a.BrokenValves, b.BrokenValves);
            Compare("trestles collapsed", a.CollapsedTrestles, b.CollapsedTrestles); Compare("buffers smashed", a.SmashedBuffers, b.SmashedBuffers);
            Compare("barricades down", a.DownBarricades, b.DownBarricades);
            Compare("spares used", a.SparesUsed, b.SparesUsed); Compare("cages open", a.OpenCages, b.OpenCages);
            if (a.KeysTaken != b.KeysTaken) diffs.Add($"orc's keys {(b.KeysTaken ? "taken" : "at the belt")} on the server");
            if (a.BridgeGone != b.BridgeGone) diffs.Add($"bridge cells gone {a.BridgeGone} -> {b.BridgeGone} on the server");
        }
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
