using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 1's stand-still pieces and the extension points for the others. A "lone" cinder and a "thrower" are spawned when their
/// ride ARMS (the cart's departure), standing where the cart will come (<see cref="SpawnStander"/>), so they are already there
/// to be seen. Set pieces (<see cref="RailSetPiecePlan"/>, kinds "beam", ...) are built once when the floor starts
/// (<see cref="BuildSetPieces"/>) and ticked while their ride is armed (<see cref="TickSetPieces"/>). A later kind adds a case to
/// each of the three switches.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>A thrower's wind-up is at least this long: its hand glows the whole time (the Shoot state glows for its wind-up).</summary>
    public const float ThrowerWindup = 1.5f;

    private sealed class Piece
    {
        public RailSetPiecePlan Plan = null!;
        public Node3D Node = null!;
        public float S;
        public bool Fired;
        public TrackSwitch? Switch;
        public OreHeap? Heap;
        public OreBuckets? Buckets;
        /// <summary>Its warning has been called this arming.</summary>
        public bool Barked;
        /// <summary>A wreck or a load undid the ride: the heap stands again at the next arming.</summary>
        public bool Rebuild;
    }

    private readonly List<Piece> _pieces = new();
    /// <summary>Stand-ins of the armed ride that go with it if the cart stops short ("lone"); a thrower is not on it.</summary>
    private readonly List<Enemy> _lones = new();
    private readonly List<(string id, Enemy enemy)> _throwers = new();
    private int _headsBarked;

    /// <summary>The set pieces built on this floor (tests and the smoke read it).</summary>
    public int SetPieceCount => _pieces.Count;
    /// <summary>Times the beam has ducked the riders (tests read it).</summary>
    public int BeamPasses { get; private set; }

    // ---- extension point 1: arming (spawn what stands on the line) ------------------------------

    /// <summary>The kinds that stand on the line from the ride's departure rather than starting as the cart reaches them.</summary>
    private static bool StandsFromDeparture(string kind) => kind is "lone" or "thrower";

    private void ArmStanders(int ride)
    {
        for (var i = 0; i < _encounters.Count; i++)
        {
            var enc = _encounters[i];
            if (enc.Ride != ride || !StandsFromDeparture(enc.Kind)) continue;
            _started[i] = true; // StartReached leaves it be
            SpawnStander(i, enc);
        }
        for (var i = 0; i < _pieces.Count; i++) if (_pieces[i].Plan.Ride == ride) _pieces[i].Fired = false;
        ArmSwitchyard(ride);
    }

    private void SpawnStander(int index, RailEncounterPlan enc)
    {
        if (enc.Riders.Count == 0 || !IsInstanceValid(_rails.Party.Track.Track)) return;
        var track = _rails.Party.Track;
        var s = RideS(enc.Ride, enc.At);
        var id = enc.Riders[0].EnemyId;
        var frame = track.Frame(s);
        // the party-lane cell nearest S: the lane's own point (its cell is checked by the smoke), standing on the rails
        var at = track.Point(s) + Vector3.Up * 0.05f;
        switch (enc.Kind)
        {
            case "lone":
            {
                if (_builder.SpawnEnemy(id, at, _builder.CurrentFloor) is not { } lone) return;
                lone.IsReinforcement = true;
                lone.HoldAt(at);
                FaceCart(lone, frame);
                _lones.Add(lone);
                GD.Print($"[rails] lone {enc.Id} ({lone.Profile.Name}) stands on cell {DungeonBuilder.WorldToCell(at)} at party S={s:0.0} (ride {enc.Ride})");
                ShowLine(index, enc);
                break;
            }
            case "thrower":
            {
                foreach (var (tid, old) in _throwers) if (tid == enc.Id && IsInstanceValid(old) && old.IsAlive) return; // still standing from an earlier try
                EnsurePockets();
                var pocket = NearestPocket(_partyPockets!, track.Point(s));
                if (pocket is not { } spot) return;
                var baseProfile = _builder.EnemyProfile(id);
                var profile = baseProfile == null ? null : baseProfile with { WindupSeconds = Mathf.Max(ThrowerWindup, baseProfile.WindupSeconds) };
                if (_builder.SpawnEnemy(id, spot, _builder.CurrentFloor, profile: profile) is not { } thrower) return;
                thrower.IsReinforcement = true;
                thrower.HoldAt(spot);
                var toward = track.Point(s) - spot;
                toward.Y = 0f;
                if (toward.LengthSquared() > 0.01f) thrower.LookAt(thrower.GlobalPosition + toward, Vector3.Up);
                _throwers.Add((enc.Id, thrower));
                GD.Print($"[rails] thrower {enc.Id} ({thrower.Profile.Name}, wind-up {thrower.Profile.WindupSeconds:0.0} s) stands in the pocket at {spot} for party S={s:0.0} (ride {enc.Ride})");
                break;
            }
        }
    }

    private static Vector3? NearestPocket(Vector3[] pockets, Vector3 to)
    {
        Vector3? best = null;
        var bestD = float.MaxValue;
        foreach (var p in pockets)
        {
            float dx = p.X - to.X, dz = p.Z - to.Z, d = dx * dx + dz * dz;
            if (d >= bestD || Mathf.Abs(p.Y - to.Y) > 3f) continue;
            bestD = d;
            best = p;
        }
        return best;
    }

    /// <summary>Face back along the line, toward the cart that comes from lower S: the frame's +Z.</summary>
    private static void FaceCart(Node3D body, Transform3D frame)
    {
        var back = frame.Basis.Z;
        back.Y = 0f;
        if (back.LengthSquared() > 0.01f) body.LookAt(body.GlobalPosition + back, Vector3.Up);
    }

    // ---- extension point 2: clearing -------------------------------------------------------------

    /// <summary>The ride is over or undone: a lone cinder the cart never reached goes silently.</summary>
    private void ClearStanders()
    {
        foreach (var e in _lones)
            if (IsInstanceValid(e) && !e.IsQueuedForDeletion()) e.QueueFree();
        _lones.Clear();
        _throwers.RemoveAll(t => !IsInstanceValid(t.enemy) || !t.enemy.IsAlive);
    }

    /// <summary>The cart is restored (a wreck undone): the throwers go too; until then they stay as any enemy would.</summary>
    private void ClearThrowers()
    {
        foreach (var (_, e) in _throwers)
            if (IsInstanceValid(e) && !e.IsQueuedForDeletion()) e.QueueFree();
        _throwers.Clear();
    }

    // ---- extension point 3: set pieces -----------------------------------------------------------

    /// <summary>Build every set piece of the floor (they are dressing until their ride is armed). Later kinds add a case.</summary>
    private void BuildSetPieces()
    {
        if (GetParent() is not Node3D root || _rails.StopS.Length < 2) return;
        foreach (var plan in _setPieces)
        {
            if (plan.Ride < 1 || plan.Ride >= _rails.StopS.Length) continue;
            var s = RideS(plan.Ride, plan.At);
            var piece = new Piece { Plan = plan, S = s };
            Node3D? node = null;
            switch (plan.Kind)
            {
                case "beam":
                    var beam = new LowBeam { Name = $"Beam_{plan.Id}", S = s };
                    root.AddChild(beam);
                    beam.Place(_rails.Party.Track.Frame(s));
                    node = beam;
                    break;
                case "switch_out":
                case "switch_back":
                    node = BuildSwitch(root, piece);
                    break;
                case "heap":
                    node = BuildHeap(root, piece);
                    break;
                case "buckets":
                    node = BuildBuckets(root, piece);
                    break;
            }
            if (node == null) continue;
            piece.Node = node;
            _pieces.Add(piece);
        }
    }

    /// <summary>The armed ride's pieces: the beam and the points act as the rolling cart's front reaches them; the heap and the buckets tick while the ride is armed (a cart halted at the heap too).</summary>
    private void TickSetPieces(PartyController party, float dt)
    {
        if (_pieces.Count == 0 || ArmedRide < 1) return;
        var rolling = _rails.State.Mode == CartMode.Rolling;
        foreach (var piece in _pieces)
        {
            if (piece.Plan.Ride != ArmedRide) continue;
            switch (piece.Plan.Kind)
            {
                case "beam":
                    if (rolling && !piece.Fired && _rails.Party.FrontS >= piece.S) { piece.Fired = true; DuckUnder(party, piece); }
                    break;
                case "switch_out":
                    if (rolling && !piece.Fired && _rails.Party.FrontS >= piece.S) TakeSwitch(piece);
                    break;
                case "heap":
                    TickHeap(piece, party, rolling);
                    break;
                case "buckets":
                    TickBuckets(piece, dt);
                    break;
            }
        }
    }

    /// <summary>The cart's front is at the beam: seated desktop heroes duck (no shake in VR), a seated companion calls it once.</summary>
    private void DuckUnder(PartyController party, Piece piece)
    {
        BeamPasses++;
        Adventurer? speaker = null;
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || !hero.Seated || hero.Cart != _rails.Cart) continue;
            if (!Vr.Active) hero.Shake(0.05f, 0.15f);
            if (speaker == null && hero.Control == ControlKind.Companion && !hero.Downed) speaker = hero;
        }
        if (speaker != null)
        {
            var name = speaker.State.Name;
            EventBus.Instance.Log($"{name}: \"Heads!\"");
            EventBus.Instance.RaiseBark(name, new Line("Heads!"));
            Main.Instance?.Hud?.ShowBark(name, "Heads!", 2f);
            _headsBarked++;
        }
        GD.Print($"[rails] beam {piece.Plan.Id} passed at S={piece.S:0.0} (cart front {_rails.Party.FrontS:0.0}); bark={(speaker != null ? speaker.State.Name : "none")}");
    }
}
