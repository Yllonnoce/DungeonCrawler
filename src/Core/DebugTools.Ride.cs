using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-ride -------------------------------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3: the line is there (five stops), the party boards and rides to stop 1
// over a small enemy, the stop survives a save and a load with nobody aboard, a companion who jumps out at speed is
// mangled while the cart rolls on (and the orc gloats with a RolledOver line, never the Press's Crushed one), and a big
// enemy on thin plating wrecks the cart (and he gloats with a CartWrecked line). Prints [ride] lines and [ride] RESULT.
public partial class DebugTools
{
    private bool _testRide;
    private int _rideStep;
    private float _rideClock;
    private bool _rideListening, _rideCinderByHazard, _rideCinderOther;
    private Enemies.Enemy? _rideCinder, _rideBrute;
    private Vector3 _rideCinderAt, _rideBruteAt;
    private float _rideSeatDrift;
    private Adventurer? _rideThrown;
    private float _rideThrownS;
    private readonly Dictionary<int, (float Hp, float Max)> _rideHpBefore = new();
    /// <summary>Every villain line heard since the floor loaded: when (seconds since start-up) and its words inside the quotes, coarse and clean.</summary>
    private readonly List<(double At, string Text, string? Clean)> _rideVillain = new();
    private double _rideThrownAt, _rideWreckedAt;

    private static double RideNow => Time.GetTicksMsec() / 1000.0;

    /// <summary>Every line the orc may say for a trigger: the campaign's own and the stock, coarse and clean.</summary>
    private static HashSet<string> RideTauntTexts(Adventure.Objectives.TauntTrigger trigger)
    {
        var id = Adventure.Objectives.TauntTriggers.Id(trigger);
        var set = new HashSet<string>();
        foreach (var t in (Game.Instance.Adventure.Campaign?.Taunts ?? new List<Adventure.TauntPlan>()).Concat(Adventure.Objectives.TauntBank.All()))
            if (t.Trigger == id) { set.Add(t.Line.Text); if (t.CleanLine != null) set.Add(t.CleanLine.Text); }
        return set;
    }

    /// <summary>The first villain line heard at or after <paramref name="since"/> (within <paramref name="within"/> s) that is one of the trigger's lines, or null.</summary>
    private string? RideTauntHeard(Adventure.Objectives.TauntTrigger trigger, double since, double within)
    {
        var texts = RideTauntTexts(trigger);
        foreach (var (at, text, clean) in _rideVillain)
            if (at >= since && at - since <= within && (texts.Contains(text) || clean != null && texts.Contains(clean))) return text;
        return null;
    }

    /// <summary>The words of a "Villain: \"…\"" narration, or null when it is not the villain's.</summary>
    private static string? RideVillainWords(string? line)
    {
        var villain = Adventure.Objectives.ObjectiveText.Villain(Game.Instance.Adventure.Campaign?.Antagonist.Name);
        if (line == null || !line.StartsWith(villain + ": \"") || !line.EndsWith('"')) return null;
        return line[(villain.Length + 3)..^1];
    }

    private void RunRideTest()
    {
        var main = Main.Instance;
        if (main?.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root || !main.InGame || Game.Instance.CurrentFloor != 12) return;
        _rideClock += (float)GetProcessDeltaTime();
        void Fail(string why) { GD.Print($"[ride] RESULT FAIL: {why}"); _done = true; GetTree().Quit(1); }
        var b = main.Builder;
        var party = main.Party;
        var rc = RailController.Instance;
        // the test owns the only enemies on the floor (as --clear-enemies): the stop mobs, ambushers and portals are culled as they appear
        if (_rideStep >= 1)
            foreach (var e in root.GetChildren().OfType<Enemies.Enemy>())
                if (e != _rideCinder && e != _rideBrute && e.IsAlive) e.Kill();
        // the test's own enemies keep to the spot on the line until the cart reaches them (flat only: they keep their feet)
        if (rc != null && rc.State.Mode == CartMode.Rolling)
        {
            Pin(_rideCinder, _rideCinderAt);
            Pin(_rideBrute, _rideBruteAt); // a hit brute is knocked aside and wrecks the cart in the same tick: never pinned back
        }

        switch (_rideStep)
        {
            case 0 when _rideClock >= 1.0f:
            {
                if (layout.Rail?.Stops.Count != 5 || rc == null || b.PartyTrack == null)
                {
                    Fail($"floor 12 has no rail line ({layout.RailFailure ?? (layout.Rail == null ? "no line" : rc == null ? $"{layout.Rail.Stops.Count} stops, no RailController" : $"{layout.Rail.Stops.Count} stops")})");
                    return;
                }
                if (!_rideListening)
                {
                    _rideListening = true;
                    EventBus.Instance.EnemyKilledByHazard += (e, _) => { if (e == _rideCinder) _rideCinderByHazard = true; };
                    EventBus.Instance.EnemyDied += e => { if (e == _rideCinder && !_rideCinderByHazard) _rideCinderOther = true; };
                    EventBus.Instance.NarrationEx += (text, _, clean, _) => { if (RideVillainWords(text) is { } words) _rideVillain.Add((RideNow, words, RideVillainWords(clean))); };
                }
                GD.Print($"[ride] 0 floor 12: stops={layout.Rail.Stops.Count} StopS=[{string.Join(", ", rc.StopS.Select(s => s.ToString("0.0")))}] length={b.PartyTrack.Length:0.0} lethality={TierRules.Lethality} heroes={party.Active.Count()}");
                _rideStep++;
                _rideClock = 0f;
                break;
            }
            case 1 when _rideClock >= 0.5f:
            {
                _rideStep++;
                _rideClock = 0f;
                if (!RideBoard(main, layout, rc!, out var why)) { Fail(why); return; }
                break;
            }
            case 2:
            {
                var heroes = party.Active.ToList();
                if (heroes.Count == 4 && heroes.All(a => a.Seated && a.Cart == rc!.Cart))
                {
                    GD.Print($"[ride] 1 boarded: {heroes.Count} seated after {_rideClock:0.00} s (seats {string.Join(",", heroes.Select(a => $"{a.State.Name}:{a.Seat}"))}) boarding={rc!.Boarding.Running} left={rc.Boarding.Left:0.0}");
                    _rideStep++;
                    _rideClock = 0f;
                }
                else if (_rideClock > 1f) Fail($"not all four heroes seated within 1 s ({heroes.Count(a => a.Seated)}/{heroes.Count})");
                break;
            }
            case 3:
            {
                if (rc!.State.Mode == CartMode.Rolling)
                {
                    GD.Print($"[ride] 2 departed after {_rideClock:0.00} s at S={rc.State.S:0.00}");
                    _rideStep++;
                    _rideClock = 0f;
                    _rideSeatDrift = 0f;
                    // a small enemy on the lane 30 m ahead: the cart must run it down on the way to stop 1
                    _rideCinder = RideSpawnAhead(main, layout, rc, "enemy.horde_cinder", 30f, out _rideCinderAt, out var cell);
                    if (_rideCinder == null) { Fail("could not spawn enemy.horde_cinder on the lane ahead"); return; }
                    _rideCinder.SetUntargetable(true); // the run-over is what this checks: seated companions shoot at any distance now; a hazard's blow ignores Untargetable, so only the cart can kill it
                    GD.Print($"[ride] 3 {_rideCinder.Name} placed on lane cell {cell} at S={rc.State.S + 30f:0.0} (stop 1 at {rc.StopS[1]:0.0})");
                }
                else if (_rideClock > 6f) Fail($"the cart did not depart within 6 s (mode {rc.State.Mode}, boarding {rc.Boarding.Running} left {rc.Boarding.Left:0.0})");
                break;
            }
            case 4:
            {
                foreach (var a in party.Active)
                    if (a.Seated && a.Control == ControlKind.Companion)
                        _rideSeatDrift = Mathf.Max(_rideSeatDrift, a.GlobalPosition.DistanceTo(a.Cart!.SeatTransform(a.Seat).Origin));
                if (_rideCinderOther) { Fail("the small enemy died, but not under the cart"); return; }
                if (rc!.State.Mode == CartMode.Held && rc.StopIndex == 1)
                {
                    var off = Mathf.Abs(rc.State.S - rc.StopS[1]);
                    GD.Print($"[ride] 3 small enemy killed by the cart: {_rideCinderByHazard} (alive={(_rideCinder != null && IsInstanceValid(_rideCinder) && _rideCinder.IsAlive)})");
                    GD.Print($"[ride] 4 held at stop 1 after {_rideClock:0.0} s: S={rc.State.S:0.000} (stop {rc.StopS[1]:0.000}, off {off:0.000}) plating={rc.State.Plating:0} max seat drift={_rideSeatDrift:0.000} m");
                    if (!_rideCinderByHazard) { Fail("the small enemy was not killed by the cart before stop 1"); return; }
                    if (off > 0.05f) { Fail($"the cart stopped {off:0.000} m from stop 1"); return; }
                    if (_rideSeatDrift > 0.1f) { Fail($"a seated companion left its seat by {_rideSeatDrift:0.000} m"); return; }
                    // brief step 5: save at the stop with everyone aboard, load
                    main.SaveGame("ridetest");
                    if (!main.LoadGame("ridetest")) { Fail("the save would not load"); return; }
                    _rideCinder = null;
                    _rideStep++;
                    _rideClock = 0f;
                }
                else if (_rideClock > 60f) Fail($"the cart was not held at stop 1 within 60 s (mode {rc.State.Mode}, S={rc.State.S:0.0}, stop index {rc.StopIndex})");
                break;
            }
            case 5 when _rideClock >= 1f:
            {
                if (rc == null) { Fail("no RailController after the load"); return; }
                var seated = party.Adventurers.Count(a => IsInstanceValid(a) && a.Seated);
                var inside = party.Active.Count(a => a.GlobalPosition.DistanceTo(rc.Cart.GlobalPosition) < 1.2f);
                var off = Mathf.Abs(rc.State.S - rc.StopS[1]);
                GD.Print($"[ride] 5 after load: mode={rc.State.Mode} stop={rc.StopIndex} S={rc.State.S:0.000} (off {off:0.000}) plating={rc.State.Plating:0} seated={seated} seats booked={rc.Cart.Seats.Count} heroes at the cart={inside} cells=[{string.Join(" ", party.Active.Select(a => DungeonBuilder.WorldToCell(a.GlobalPosition)))}]");
                if (rc.State.Mode != CartMode.Held || rc.StopIndex != 1 || off > 0.05f) { Fail("the cart did not come back held at stop 1"); return; }
                if (seated > 0 || rc.Cart.Seats.Count > 0) { Fail("somebody is still seated after the load"); return; }
                if (inside > 0) { Fail("a hero was set down inside the parked cart"); return; }
                _rideStep++;
                _rideClock = 0f;
                if (!RideBoard(main, layout, rc, out var why)) { Fail(why); return; }
                break;
            }
            case 6:
            {
                if (rc!.State.Mode != CartMode.Rolling)
                {
                    if (_rideClock > 8f) Fail($"re-boarded, but the cart did not depart (mode {rc.State.Mode}, seated {party.Active.Count(a => a.Seated)})");
                    break;
                }
                _rideStep++;
                _rideClock = 0f;
                GD.Print($"[ride] 6 re-boarded and departed with {party.Active.Count(a => a.Seated)} seated");
                break;
            }
            case 7 when _rideClock >= 2f:
            {
                if (rc!.State.V < RunOverRules.ThrownAtSpeed)
                {
                    if (_rideClock > 8f) Fail($"the cart never reached {RunOverRules.ThrownAtSpeed} m/s (V={rc.State.V:0.00})");
                    break;
                }
                var mate = party.Active.FirstOrDefault(a => a.Seated && a.Control == ControlKind.Companion);
                if (mate == null) { Fail("no seated companion to jump out"); return; }
                var v = rc.State.V;
                _rideThrownAt = RideNow; // the mangling (and the orc's line for it) comes inside the get-out call
                rc.Cart.Interact(party, mate); // the controller's get-out path, at speed
                _rideThrown = mate;
                _rideThrownS = rc.State.S;
                GD.Print($"[ride] 6 {mate.State.Name} gets out at V={v:0.00} m/s, {_rideClock:0.0} s into the ride: seated={mate.Seated} downed={mate.Downed} mangled={mate.State.Mangled} hp={mate.State.Hp:0}");
                if (mate.Seated || !mate.Downed || !mate.State.Mangled) { Fail($"{mate.State.Name} jumped from a moving cart and was not mangled"); return; }
                _rideStep++;
                _rideClock = 0f;
                break;
            }
            case 8 when _rideClock >= 1f:
            {
                var still = party.Active.Count(a => a.Seated);
                GD.Print($"[ride] 6 one second later: mode={rc!.State.Mode} V={rc.State.V:0.00} S {_rideThrownS:0.0} -> {rc.State.S:0.0}, still seated={still}");
                if (rc.State.Mode != CartMode.Rolling || rc.State.S <= _rideThrownS + 0.5f) { Fail("the cart did not roll on after the jump"); return; }
                if (still != 3) { Fail($"{still} still seated (the others should ride on)"); return; }
                // the orc gloats over the run-over as a cart's, within a second, and never as the Press's
                if (RideTauntHeard(Adventure.Objectives.TauntTrigger.Crushed, _rideThrownAt, double.PositiveInfinity) is { } pressed) { Fail($"the run-over was taunted as a crushing: \"{pressed}\""); return; }
                if (RideTauntHeard(Adventure.Objectives.TauntTrigger.RolledOver, _rideThrownAt, 1.0) is not { } rolled)
                { Fail($"no RolledOver taunt within 1 s of {_rideThrown?.State.Name} being run over (villain lines since: {_rideVillain.Count(x => x.At >= _rideThrownAt)})"); return; }
                GD.Print($"[ride] taunt RolledOver \"{rolled}\"");
                // step 7: thin plating and a big enemy ahead: the cart is wrecked
                rc.DebugSetPlating(15f);
                _rideBrute = RideSpawnAhead(main, layout, rc, "enemy.horde_cinder_brute", 20f, out _rideBruteAt, out var cell);
                if (_rideBrute == null) { Fail("could not spawn enemy.horde_cinder_brute on the lane ahead"); return; }
                RideNoteHp(party, rc);
                GD.Print($"[ride] 7 plating set to {rc.State.Plating:0}; {_rideBrute.Name} (scale {_rideBrute.Profile.Scale}) on lane cell {cell} at S={rc.State.S + 20f:0.0}");
                _rideStep++;
                _rideClock = 0f;
                break;
            }
            case 9:
            {
                if (rc!.State.Mode == CartMode.Rolling) { RideNoteHp(party, rc); if (_rideClock > 10f) Fail($"the cart was not wrecked within 10 s (plating {rc.State.Plating:0}, S={rc.State.S:0.0})"); break; }
                // a spare cart takes over at once: the wreck leaves the cart Held at a stop, full plating, one spare spent
                if (rc.State.Mode != CartMode.Held || rc.State.Plating < CartRules.MaxPlating - 0.01f || rc.SparesUsed.Count != 1)
                { Fail($"after the wreck the cart is {rc.State.Mode} (plating {rc.State.Plating:0}, spares used {rc.SparesUsed.Count}); a spare should hold it at a stop"); return; }
                var fails = new List<string>();
                var lines = new List<string>();
                foreach (var a in party.Active)
                {
                    if (!_rideHpBefore.TryGetValue(a.Index, out var before)) continue;
                    var lost = (before.Hp - a.State.Hp) / before.Max;
                    var cell = DungeonBuilder.WorldToCell(a.GlobalPosition);
                    lines.Add($"{a.State.Name}: hp {before.Hp:0}->{a.State.Hp:0}/{before.Max:0} lost {lost * 100f:0.0}% seated={a.Seated} cell={cell} rail={layout.IsRail(cell)}");
                    if (a.Seated) fails.Add($"{a.State.Name} is still seated");
                    if (Mathf.Abs(lost - 0.25f) > 0.02f) fails.Add($"{a.State.Name} lost {lost * 100f:0.0}% of max HP");
                    if (layout.IsRail(cell)) fails.Add($"{a.State.Name} stands on a rail cell {cell}");
                }
                GD.Print($"[ride] 7 wrecked after {_rideClock:0.0} s at S={rc.State.S:0.0}: {string.Join("; ", lines)}; brute alive={_rideBrute != null && IsInstanceValid(_rideBrute) && _rideBrute.IsAlive}");
                if (party.Adventurers.Any(a => IsInstanceValid(a) && a.Seated)) fails.Add("a hero is still seated");
                if (rc.StopIndex < 1 || rc.StopIndex > rc.StopS.Length - 2) fails.Add($"the spare stands at stop {rc.StopIndex}, not an intermediate stop");
                if (_rideHpBefore.Count == 0) fails.Add("nobody was seated when the cart wrecked");
                if (fails.Count > 0) { Fail(string.Join("; ", fails)); return; }
                _rideWreckedAt = RideNow - Mathf.Max(0.5f, (float)GetProcessDeltaTime() * 2f); // the wreck was judged in a physics tick just before this frame
                _rideStep++;
                _rideClock = 0f;
                break;
            }
            case 10:
            {
                // the orc gloats over the wreck (the cart's Wrecked event, told at its flush)
                if (RideTauntHeard(Adventure.Objectives.TauntTrigger.CartWrecked, _rideWreckedAt, 2.0) is { } wrecked)
                {
                    GD.Print($"[ride] taunt CartWrecked \"{wrecked}\"");
                    GD.Print("[ride] RESULT ok");
                    _done = true;
                    GetTree().Quit();
                }
                else if (_rideClock > 1f) Fail($"no CartWrecked taunt within 1 s of the wreck (villain lines: {string.Join(" | ", _rideVillain.Select(x => x.Text))})");
                break;
            }
        }
    }

    /// <summary>Companions to Hold (they fight only what is within 3 m, so the cart gets the test's enemies), the party beside the cart, the lead uses it.</summary>
    private bool RideBoard(Main main, FloorLayout layout, RailController rc, out string why)
    {
        why = "";
        var party = main.Party;
        foreach (var a in party.Adventurers) if (IsInstanceValid(a) && a.Control == ControlKind.Companion) a.Stance = Characters.Stance.Hold;
        var at = rc.Cart.GlobalPosition;
        var here = DungeonBuilder.WorldToCell(at);
        GridPos? beside = null;
        var bestD = float.MaxValue;
        for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                if (!layout.IsWalkable(c) || layout.IsRail(c) || main.Builder.IsCellBlocked(c)) continue; // a cage wagon's siding is no place to stand
                var d = main.Builder.Surface(c).DistanceTo(at);
                if (d < bestD) { bestD = d; beside = c; }
            }
        if (beside is not { } cell) { why = $"no walkable cell beside the cart at {here}"; return false; }
        party.Teleport(main.Builder.Surface(cell) + Vector3.Up * 0.3f, at);
        if (party.Local is not { } lead) { why = "no local hero to board"; return false; }
        var prompt = rc.Cart.Prompt;
        rc.Cart.Interact(party, lead);
        GD.Print($"[ride] {lead.State.Name} uses the cart from {cell} ({bestD:0.0} m) prompt=\"{prompt}\" stop={rc.StopIndex}");
        return true;
    }

    /// <summary>An enemy on the party's line <paramref name="ahead"/> metres in front of the cart (on the track's own centreline, so on a lane cell).</summary>
    private Enemies.Enemy? RideSpawnAhead(Main main, FloorLayout layout, RailController rc, string id, float ahead, out Vector3 at, out GridPos cell)
    {
        var track = main.Builder.PartyTrack!;
        var s = Mathf.Min(track.Length, rc.State.S + ahead);
        at = track.ToGlobal(track.Curve.SampleBaked(s, true));
        cell = DungeonBuilder.WorldToCell(at);
        if (layout.Rail?.Party.Contains(cell) != true) GD.Print($"[ride] note: the line at S={s:0.0} is over cell {cell}, not a party lane cell");
        return main.Builder.SpawnEnemy(id, at + Vector3.Up * 0.3f, main.Builder.CurrentFloor);
    }

    private static void Pin(Enemies.Enemy? e, Vector3 at)
    {
        if (e == null || !IsInstanceValid(e) || !e.IsAlive) return;
        e.GlobalPosition = new Vector3(at.X, e.GlobalPosition.Y, at.Z);
        e.Velocity = Vector3.Zero;
    }

    /// <summary>The seated heroes' HP as it stands, to measure the wreck's toll against.</summary>
    private void RideNoteHp(PartyController party, RailController rc)
    {
        _rideHpBefore.Clear();
        foreach (var a in party.Active)
            if (a.Cart == rc.Cart && !a.Downed) _rideHpBefore[a.Index] = (a.State.Hp, a.State.Stats[Characters.Stat.MaxHp]);
    }
}
