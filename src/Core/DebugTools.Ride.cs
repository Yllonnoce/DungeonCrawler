using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Rails;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-ride -------------------------------------------------------------------------------------------
// The built-in campaign's floor 12 at lethality 3: the line is there (six stops, five rides; no hammer's or pour's cue reaches a stop), the party boards and rides to stop 1
// over a small enemy, the stop survives a save and a load with nobody aboard, a companion who jumps out at speed is
// mangled while the cart rolls on (and the orc gloats with a RolledOver line, never the Press's Crushed one), and a big
// enemy on thin plating wrecks the cart (and he gloats with a CartWrecked line). The first departure's RideStart taunt is said
// or held, and a seated companion (never the player's hero beside one) calls "all aboard" once at the landing and once more
// at the re-boarding after the load.
// Prints [ride] lines and [ride] RESULT.
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
    /// <summary>The AllAboard calls heard (RailCalls.Called), said or only logged.</summary>
    private int _rideAllAboard;
    /// <summary>A ride call said by the player's own hero while a companion sat up in the same cart (a companion should have said it), or null.</summary>
    private string? _ridePlayerCalled;

    private static double RideNow => Time.GetTicksMsec() / 1000.0;

    /// <summary>
    /// The solo ride flows (--test-ride, --test-ride-edges, --test-chase, --test-cart-watchdog) depart stops with the cage still locked:
    /// every frame, once the cage holds the cart with the local hero seated in it, the hero leaves the prisoners behind (the held
    /// interact), so the countdown starts as the arrival used to start it, with the same call. --test-crew checks the hold itself.
    /// </summary>
    internal static void RideWaiveCages(Main main, RailController rc, string tag)
    {
        if (!rc.CageHeld || main.Party?.Local is not { } me || me.Cart != rc.Cart) return;
        var stop = rc.StopIndex;
        rc.Cart.HoldInteract(main.Party, me);
        if (!rc.CageHeld) GD.Print($"[{tag}] leaves the prisoners at stop {stop} behind (the cage hold is --test-crew's to check)");
    }

    // ---- the ride's sound (plan 6 task 5): what played where, for --test-ride and --test-crew --------------------------------------

    private bool _soundListening;
    /// <summary>Every one-shot heard since the test began: when, its cue, and whether a cart's sound played it (else a positional PlayAt). Ambient plays are left out.</summary>
    private readonly List<(double At, string Cue, bool Cart)> _soundPlays = new();
    /// <summary>Rail cues raised on the non-positional path (EventBus.Sfx): there must be none.</summary>
    private readonly List<string> _soundRelayed = new();
    private double _rideDepartAt = -1, _rideLoadAt = -1, _rideWreckSeenAt = -1;
    private float _rideMaxV;
    private bool _rideRollChecked, _ridePitchChecked;
    private int _ridePitchSamples;
    /// <summary>The ride's music (plan 6 task 6): when the cart was first held at stop 1 (a wall clock that outlives the load), and which checks have run.</summary>
    private double _rideArriveAt = -1;
    private bool _rideMusicOn, _rideMusicOff;
    /// <summary>The Chase music must be gone this long after the load at stop 1: its hold, one 0.5 s poll of MusicDirector's, and 0.25 s for frame jitter.</summary>
    private const double MusicOffAfterLoad = RideMusic.HoldSeconds + 0.5 + 0.25;

    private void SoundListen()
    {
        if (_soundListening) return;
        _soundListening = true;
        Action<string, Vector3, bool> playedAt = (cue, _, ambient) => { if (!ambient) _soundPlays.Add((RideNow, cue, false)); };
        Action<string, Vector3> cartPlayed = (cue, _) => _soundPlays.Add((RideNow, cue, true));
        Audio.AudioManager.PlayedAt += playedAt;
        CartSound.Played += cartPlayed;
        _staticUnhooks.Add(() => { Audio.AudioManager.PlayedAt -= playedAt; CartSound.Played -= cartPlayed; });
        EventBus.Instance.Sfx += cue => { if (RideSound.Clips.ContainsKey(cue)) _soundRelayed.Add(cue); };
    }

    private int SoundCount(string cue, double from, double to, bool? cart = null)
        => _soundPlays.Count(p => p.Cue == cue && p.At >= from && p.At <= to && (cart == null || p.Cart == cart));

    /// <summary>The plays of <paramref name="cues"/> within <paramref name="seconds"/> after <paramref name="since"/>, as "cue@+t" words (empty: none).</summary>
    private List<string> SoundsAfter(double since, double seconds, params string[] cues)
        => _soundPlays.Where(p => p.At >= since && p.At <= since + seconds && cues.Contains(p.Cue)).Select(p => $"{p.Cue}@+{p.At - since:0.00}s").ToList();

    /// <summary>
    /// Every stop's place on the party line against every drop hammer and the pour: each piece's cue range (its own, set at the build) must
    /// end at least 2 m short of a cart held at any stop. Prints the nearest of each to any stop; null when every piece passes, else why not.
    /// </summary>
    private static string? StopsCheck(string tag, Node root, RailController rc)
    {
        if (rc.StopS.Length < 2 || Main.Instance?.Builder.PartyTrack is not { } track) return null;
        var pieces = new List<(string What, Node3D Node, float Range)>();
        foreach (var child in root.GetChildren())
        {
            if (child is MoltenPour p) pieces.Add(("pour", p, p.CueRange));
            else if (child is Node3D holder)
                foreach (var h in holder.GetChildren().OfType<DropHammer>()) pieces.Add(($"hammer {h.Index}", h, h.CueRange));
        }
        if (pieces.Count == 0) return "no drop hammer or pour on the floor to check";
        var checks = new List<(string What, int Stop, float Distance, float Range)>();
        for (var k = 0; k < rc.StopS.Length; k++)
        {
            var at = track.ToGlobal(track.Curve.SampleBaked(rc.StopS[k], true));
            foreach (var (what, node, range) in pieces) checks.Add((what, k, node.GlobalPosition.DistanceTo(at), range));
        }
        string Nearest(string kind) => checks.Where(x => x.What.StartsWith(kind)).OrderBy(x => x.Distance - x.Range).Select(x => $"{x.What} {x.Distance:0.0} m from stop {x.Stop} (range {x.Range:0.0} m)").FirstOrDefault() ?? "none";
        GD.Print($"[{tag}] sound: the {rc.StopS.Length} stops against the hazards' cues: nearest {Nearest("hammer")}, {Nearest("pour")}; each range is min(30, distance - 2)");
        // a hair under 2 m for float rounding
        var near = checks.Where(x => x.Distance - x.Range < SetPieceRules.StopMargin - 0.01f).Select(x => $"{x.What} {x.Distance:0.00} m from stop {x.Stop} against range {x.Range:0.00} m").ToList();
        return near.Count == 0 ? null : $"a hazard's cue reaches within 2 m of a stop: {string.Join(", ", near)}";
    }

    /// <summary>Each frame of the ride: the party cart's roll follows the rules (on above a crawl, its pitch RideSound.RollPitch of V). Null, or why not.</summary>
    private string? RideRollCheck(RailController rc)
    {
        if (rc.State.Mode != CartMode.Rolling || rc.Cart.Sound is not { } sound) return null;
        var v = rc.State.V;
        _rideMaxV = Mathf.Max(_rideMaxV, v);
        _ridePitchSamples++;
        if (v > 0.3f) { _rideRollChecked = true; if (sound.RollDbNow <= -80f) return $"the roll is silent at V={v:0.00} (RollDbNow {sound.RollDbNow:0.0})"; }
        if (v > 5.5f) { _ridePitchChecked = true; if (sound.RollPitchNow <= 1.0f) return $"the roll's pitch is {sound.RollPitchNow:0.000} at V={v:0.00} (want over 1.0)"; }
        if (Mathf.Abs(sound.RollPitchNow - RideSound.RollPitch(v)) > 0.02f) return $"the roll's pitch is {sound.RollPitchNow:0.000} at V={v:0.00}, the rule says {RideSound.RollPitch(v):0.000}";
        return null;
    }

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
        if (rc != null) RideWaiveCages(main, rc, "ride");
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

        if (rc != null && _rideStep is >= 3 and <= 9 && RideRollCheck(rc) is { } rollWrong) { Fail(rollWrong); return; }
        if (RideMusicCheck(rc) is { } musicWrong) { Fail(musicWrong); return; }

        switch (_rideStep)
        {
            case 0 when _rideClock >= 1.0f:
            {
                if (layout.Rail?.Stops.Count != Floor12Stops || rc == null || b.PartyTrack == null)
                {
                    Fail($"floor 12 has no rail line ({layout.RailFailure ?? (layout.Rail == null ? "no line" : rc == null ? $"{layout.Rail.Stops.Count} stops, no RailController" : $"{layout.Rail.Stops.Count} stops")})");
                    return;
                }
                SoundListen();
                if (StopsCheck("ride", root, rc) is { } near) { Fail(near); return; }
                if (!_rideListening)
                {
                    _rideListening = true;
                    EventBus.Instance.EnemyKilledByHazard += (e, _) => { if (e == _rideCinder) _rideCinderByHazard = true; };
                    EventBus.Instance.EnemyDied += e => { if (e == _rideCinder && !_rideCinderByHazard) _rideCinderOther = true; };
                    EventBus.Instance.NarrationEx += (text, _, clean, _) => { if (RideVillainWords(text) is { } words) _rideVillain.Add((RideNow, words, RideVillainWords(clean))); };
                    Action<Adventure.BarkTrigger, string?> called = (trigger, by) =>
                    {
                        if (_done || !_testRide) return;
                        if (Main.Instance?.Party is { Local: { } me } p && by == me.State.Name && me.Cart is { } cart
                            && p.Adventurers.Any(a => IsInstanceValid(a) && a != me && a.Cart == cart && !a.Downed && a.Control == ControlKind.Companion))
                            _ridePlayerCalled ??= $"{trigger} by {by}";
                        if (trigger != Adventure.BarkTrigger.AllAboard) return;
                        _rideAllAboard++;
                        GD.Print($"[ride] call {trigger} #{_rideAllAboard} by {by ?? "the log"} at step {_rideStep}");
                    };
                    RailCalls.Called += called;
                    _staticUnhooks.Add(() => RailCalls.Called -= called);
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
                    GD.Print($"[ride] 2 departed after {_rideClock:0.00} s at S={rc.State.S:0.00}; ride-start taunt said or held={rc.RideStartTaunted}");
                    _rideDepartAt = RideNow;
                    if (rc.RideStartTaunted != true) { Fail($"the first departure's RideStart taunt was {(rc.RideStartTaunted == null ? "never asked" : "dropped")}"); return; }
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
                    if (_rideAllAboard != 1) { Fail($"{_rideAllAboard} AllAboard calls before the load, not 1"); return; }
                    var clacks = SoundCount("rail_clack", _rideDepartAt, _rideDepartAt + 10.0, cart: true);
                    var squeals = SoundCount("brake_squeal", _rideDepartAt, RideNow, cart: true);
                    GD.Print($"[ride] sound: {clacks} rail_clack from the cart within 10 s of the departure; the roll checked on {_ridePitchSamples} frame(s), V up to {_rideMaxV:0.00}, roll on above 0.3 m/s {(_rideRollChecked ? "yes" : "NO")}, pitch over 1.0 above 5.5 m/s {(_ridePitchChecked ? "yes" : "not reached")}");
                    GD.Print($"[ride] sound: the arrival at stop 1 played brake_squeal x{squeals}");
                    if (clacks < 5) { Fail($"only {clacks} rail_clack from the cart within 10 s of the departure, not 5"); return; }
                    if (squeals < 1) { Fail("the cart rolled into stop 1 without a brake_squeal"); return; }
                    if (!_rideRollChecked) { Fail("the cart never rolled above 0.3 m/s to check its roll"); return; }
                    _rideArriveAt = RideNow;
                    // brief step 5: save at the stop with everyone aboard, load
                    main.SaveGame("ridetest");
                    if (!main.LoadGame("ridetest")) { Fail("the save would not load"); return; }
                    _rideLoadAt = RideNow;
                    _rideCinder = null;
                    _rideStep++;
                    _rideClock = 0f;
                }
                else if (_rideClock > 60f) Fail($"the cart was not held at stop 1 within 60 s (mode {rc.State.Mode}, S={rc.State.S:0.0}, stop index {rc.StopIndex})");
                break;
            }
            case 5 when _rideClock >= 1f && _rideMusicOff: // the music check after the load runs first: the full party re-departs 2 s after it boards
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
                GD.Print($"[ride] 6 re-boarded and departed with {party.Active.Count(a => a.Seated)} seated; AllAboard calls {_rideAllAboard}");
                if (_rideAllAboard != 2) { Fail($"{_rideAllAboard} AllAboard calls by the re-boarding's departure, not 2 (one at the landing, one more at the re-boarding)"); return; }
                if (_ridePlayerCalled != null) { Fail($"the player's hero called the ride with a companion seated beside them ({_ridePlayerCalled})"); return; }
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
                _rideWreckSeenAt = RideNow;
                _rideStep++;
                _rideClock = 0f;
                break;
            }
            case 10 when _rideClock >= 1.05f: // a second after the wreck: its sounds are all in
            {
                // the orc gloats over the wreck (the cart's Wrecked event, told at its flush)
                if (RideTauntHeard(Adventure.Objectives.TauntTrigger.CartWrecked, _rideWreckedAt, 2.0) is { } wrecked)
                {
                    GD.Print($"[ride] taunt CartWrecked \"{wrecked}\"");
                    var wrecks = SoundCount("cart_wreck", _rideWreckSeenAt - 1.0, _rideWreckSeenAt + 1.0);
                    var squeals = SoundCount("brake_squeal", _rideWreckSeenAt - 1.0, _rideWreckSeenAt + 1.0);
                    var afterLoad = SoundsAfter(_rideLoadAt, 2.0, "hammer_wind", "pour_hiss");
                    GD.Print($"[ride] sound: the wreck with a spare played cart_wreck x{wrecks}, brake_squeal x{squeals} within a second; after the load [{string.Join(", ", afterLoad)}]; rail cues relayed on the plain path [{string.Join(", ", _soundRelayed)}]; one-shots heard {_soundPlays.Count}");
                    if (wrecks != 1 || squeals != 0) { Fail($"the wreck with a spare played cart_wreck x{wrecks} and brake_squeal x{squeals} (want 1 and 0)"); return; }
                    if (afterLoad.Count > 0) { Fail($"a hazard's cue played within 2 s of the load: {string.Join(", ", afterLoad)}"); return; }
                    if (_soundRelayed.Count > 0) { Fail($"rail cues went through EventBus.Sfx: {string.Join(", ", _soundRelayed)}"); return; }
                    GD.Print("[ride] RESULT ok");
                    _done = true;
                    GetTree().Quit();
                }
                else Fail($"no CartWrecked taunt within 1 s of the wreck (villain lines: {string.Join(" | ", _rideVillain.Select(x => x.Text))})");
                break;
            }
        }
    }

    /// <summary>
    /// The ride's music, each frame: the Chase mood within 1 s of the first departure, and gone <see cref="MusicOffAfterLoad"/> after the
    /// save and load at stop 1 (the cart held there then; step 5 boards again only after this check: a full party departs 2 s after
    /// boarding). Timed from the load, not the arrival: the hold runs on the music's own clock from its last poll of the rolling cart,
    /// before the arrival, and that clock stands nearly still through the load's long frame, so the wall time the load takes is not
    /// hold time. Null, or why not.
    /// </summary>
    private string? RideMusicCheck(RailController? rc)
    {
        var music = Audio.MusicDirector.Instance;
        if (!_rideMusicOn && _rideDepartAt >= 0)
        {
            var since = RideNow - _rideDepartAt;
            if (music is { Mood: Audio.Mood.Chase })
            {
                _rideMusicOn = true;
                GD.Print($"[ride] music {music.Mood} {music.CurrentTrack} ({since:0.00} s after the departure)");
            }
            else if (since > 1.0) return $"the music is {music?.Mood.ToString() ?? "absent"}, not Chase, {since:0.00} s after the departure";
        }
        if (!_rideMusicOff && _rideLoadAt >= 0 && RideNow - _rideLoadAt >= MusicOffAfterLoad)
        {
            _rideMusicOff = true;
            var mode = rc?.State.Mode.ToString() ?? "no controller";
            GD.Print($"[ride] music {music?.Mood.ToString() ?? "absent"} {music?.CurrentTrack} ({RideNow - _rideLoadAt:0.00} s after the load at stop 1, {RideNow - _rideArriveAt:0.00} s after the arrival, cart {mode})");
            if (music == null || music.Mood == Audio.Mood.Chase) return $"the music is still {music?.Mood.ToString() ?? "absent"} {MusicOffAfterLoad:0.00} s after the load at stop 1 (the hold and one poll)";
            if (rc == null || rc.State.Mode != CartMode.Held || rc.StopIndex != 1) return $"the cart was not held at stop 1 {MusicOffAfterLoad:0.00} s after the load (mode {mode}, stop {rc?.StopIndex})";
        }
        return null;
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
