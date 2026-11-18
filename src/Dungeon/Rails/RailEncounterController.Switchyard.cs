using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Ride 3's pieces. A "switch_out" lever stands between the lanes <see cref="LeverLead"/> metres short of its points: thrown
/// when the cart's front reaches the points, the cart crosses onto the enemy lane (<see cref="RailController.Cross"/>) and a
/// "switch_back" (always thrown, untouchable) brings it home. On the enemy lane a slag cart ahead cannot outrun the party's
/// (<see cref="RamCloseIn"/>) and is rammed when the front reaches its back end: it derails, the plating takes
/// <see cref="RamDent"/>. A "heap" blocks the party lane (<see cref="RailController.BlockLine"/>) until it is cleared; its
/// lantern and a companion's call warn first. "buckets" tip cinders onto the line: a cart through a live patch loses
/// <see cref="PatchDent"/> plating and each seated hero takes one lick of <see cref="TrapRules.FloorFire"/>. Server only.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>The lever stands this far short of its points, so a rider can pull it in passing before the cart takes them.</summary>
    public const float LeverLead = 5f;
    /// <summary>The lever stands this far from the party lane's centre, toward the enemy lane (the lanes are one cell apart).</summary>
    public const float LeverOut = 2f;
    public const float HeapDent = 40f;
    public const float RamDent = 10f;
    public const float PatchDent = 5f;
    /// <summary>A slag cart ahead on the lane the party's cart took runs at most this much slower than it: it is caught.</summary>
    public const float RamCloseIn = 2f;
    /// <summary>The party cart's front within this of a slag cart's back end (on the enemy lane) rams it.</summary>
    const float RamReach = 0.25f;
    /// <summary>A slag cart started while the party's cart is on its lane starts at least this far ahead of the front.</summary>
    const float SpawnClear = 3f;
    /// <summary>Timber posts this close to a crossover's path (or a lever) are taken down: the cart would pass through them.</summary>
    const float TimberClear = 1.1f;

    /// <summary>Slag carts rammed off the line (tests and the smoke read it).</summary>
    public int Rams { get; private set; }
    private readonly List<ICartRider> _burnt = new();

    /// <summary>A set piece's node by its plan id (the smoke and tests read it), or null.</summary>
    internal Node3D? SetPieceNode(string id)
    {
        foreach (var p in _pieces) if (p.Plan.Id == id) return p.Node;
        return null;
    }

    /// <summary>Test hook (--test-crew): throw ride <paramref name="ride"/>'s switch_out levers that are not thrown. Returns how many.</summary>
    internal int DebugThrowSwitches(int ride)
    {
        var n = 0;
        foreach (var p in _pieces)
            if (p.Plan.Ride == ride && p.Plan.Kind == "switch_out" && p.Switch is { Thrown: false } sw) { sw.DebugThrow(); n++; }
        return n;
    }

    // ---- building --------------------------------------------------------------------------------

    private Node3D? BuildSwitch(Node3D root, Piece piece)
    {
        var party = _rails.Party.Track;
        var back = piece.Plan.Kind == "switch_back";
        var leverS = Mathf.Max(0f, piece.S - LeverLead);
        var frame = party.Frame(leverS);
        var x = frame.Basis.X.Normalized();
        var side = (_enemy.Point(Lanes.EnemyS(leverS)) - frame.Origin).Dot(x) >= 0f ? 1f : -1f; // between the lanes
        var sw = new TrackSwitch { Name = $"Switch_{piece.Plan.Id}", PointsS = piece.S, Fixed = back };
        root.AddChild(sw);
        sw.GlobalTransform = new Transform3D(frame.Basis.Orthonormalized(), frame.Origin + x * (side * LeverOut));
        piece.Switch = sw;

        // the crossover rails: dressing along the path the cart takes when it is set
        var points = new List<Vector3>();
        for (var u = -1f; u <= RailController.SwitchStretch + 1f; u += 0.5f)
        {
            var s = piece.S + u;
            var t = Mathf.Clamp(u / RailController.SwitchStretch, 0f, 1f);
            var blend = t * t * (3f - 2f * t);
            if (back) blend = 1f - blend;
            points.Add(party.Point(s).Lerp(_enemy.Point(Lanes.EnemyS(s)), blend));
        }
        var rails = root.GetNodeOrNull<Node3D>("Rails") ?? root;
        var crossover = new RailTrack { Name = $"Crossover_{piece.Plan.Id}" };
        rails.AddChild(crossover);
        crossover.Build(points, _steel ??= new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.18f, 0.2f), Metallic = 0.8f, Roughness = 0.45f },
            _sleeper ??= new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.21f, 0.13f), Roughness = 0.95f });
        points.Add(sw.GlobalPosition); // and nothing stands in the lever's way
        ClearTimber(root, points);
        GD.Print($"[rails] switch {piece.Plan.Id} points at party S={piece.S:0.0}, lever at S={leverS:0.0} side {side:+0;-0} ({(back ? "fixed, thrown" : "straight")})");
        return sw;
    }

    private StandardMaterial3D? _steel, _sleeper;

    /// <summary>Take down the timber frames whose posts stand within <see cref="TimberClear"/> of <paramref name="path"/> (posts and cap together).</summary>
    private static void ClearTimber(Node3D root, List<Vector3> path)
    {
        if (root.GetNodeOrNull<Node3D>("Rails/Timbering") is not { } frames) return;
        bool Near(Vector3 p)
        {
            foreach (var q in path)
            {
                float dx = p.X - q.X, dz = p.Z - q.Z;
                if (dx * dx + dz * dz < TimberClear * TimberClear) return true;
            }
            return false;
        }
        var ends = new List<Vector3>();
        foreach (var child in frames.GetChildren())
        {
            if (child is not MeshInstance3D { Mesh: BoxMesh box } cap || box.Size.X < 1f) continue; // a cap spans the lane cell
            var along = cap.GlobalBasis.X.Normalized() * (box.Size.X / 2f - 0.15f);
            Vector3 a = cap.GlobalPosition + along, b = cap.GlobalPosition - along;
            if (!Near(a) && !Near(b)) continue;
            cap.Visible = false;
            ends.Add(a);
            ends.Add(b);
        }
        foreach (var child in frames.GetChildren())
        {
            if (child is not MeshInstance3D { Mesh: BoxMesh box } post || box.Size.X >= 1f) continue;
            var p = post.GlobalPosition;
            var hide = Near(p);
            foreach (var e in ends) if (!hide && (e.X - p.X) * (e.X - p.X) + (e.Z - p.Z) * (e.Z - p.Z) < 0.5f * 0.5f) hide = true;
            if (hide) post.Visible = false;
        }
    }

    private Node3D? BuildHeap(Node3D root, Piece piece)
    {
        var heap = new OreHeap { Name = $"Heap_{piece.Plan.Id}", S = piece.S };
        root.AddChild(heap);
        heap.GlobalTransform = _rails.Party.Track.Frame(piece.S).Orthonormalized();
        heap.Cleared += h => OnHeapCleared(piece, h);
        piece.Heap = heap;
        return heap;
    }

    private Node3D? BuildBuckets(Node3D root, Piece piece)
    {
        var buckets = new OreBuckets { Name = $"Buckets_{piece.Plan.Id}", S = piece.S, Floor = _builder.CurrentFloor };
        root.AddChild(buckets);
        buckets.Place(_rails.Party.Track);
        piece.Buckets = buckets;
        return buckets;
    }

    // ---- arming and clearing ---------------------------------------------------------------------

    /// <summary>Ride <paramref name="ride"/> arms: a heap undone by a wreck or a load stands again and blocks the line, the buckets start.</summary>
    private void ArmSwitchyard(int ride)
    {
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != ride) continue;
            p.Barked = false;
            if (p.Switch != null) p.Switch.Taken = false;
            if (p.Heap is { } heap)
            {
                if (p.Rebuild && !heap.IsAlive) heap.Rebuild();
                p.Rebuild = false;
                if (heap.IsAlive) _rails.BlockLine(heap.FaceS, HeapDent);
            }
            p.Buckets?.Arm();
        }
    }

    /// <summary>The ride's live pieces stop (any clear, the departure's too): the cinders go, the heap's lantern stops blinking.</summary>
    private void ClearSwitchyard()
    {
        foreach (var p in _pieces)
        {
            p.Buckets?.Clear();
            if (p.Heap is { } heap) heap.Alarm = false;
        }
    }

    /// <summary>The ride is over or undone (arrived, wrecked, restored, on foot): the levers fall back to straight; a wreck or a load stacks the heap again at the next arming.</summary>
    private void ResetSwitchyard(bool undone)
    {
        foreach (var p in _pieces)
        {
            p.Switch?.Reset();
            if (undone && p.Heap != null) p.Rebuild = true;
        }
    }

    private void OnHeapCleared(Piece piece, OreHeap heap)
    {
        GD.Print($"[rails] heap {piece.Plan.Id} cleared (ride {piece.Plan.Ride}, armed {ArmedRide}); cart {_rails.State.Mode} halted={_rails.Halted} S={_rails.State.S:0.0}");
        if (ArmedRide == piece.Plan.Ride && Mathf.IsEqualApprox(_rails.BlockedAt, heap.FaceS)) _rails.ClearBlock();
    }

    // ---- ticking ---------------------------------------------------------------------------------

    /// <summary>The front reached the points: a thrown lever sends the cart across (and the next switch_back of the ride brings it home).</summary>
    private void TakeSwitch(Piece piece)
    {
        piece.Fired = true;
        if (piece.Switch != null) piece.Switch.Taken = true; // its points are behind the cart: nothing left to pull this ride
        if (piece.Switch is not { Thrown: true }) { GD.Print($"[rails] switch {piece.Plan.Id} passed straight at S={_rails.State.S:0.0}"); return; }
        var backS = float.NaN;
        foreach (var p in _pieces)
            if (p.Plan.Ride == piece.Plan.Ride && p.Plan.Kind == "switch_back" && p.S > piece.S && (float.IsNaN(backS) || p.S < backS)) backS = p.S;
        _rails.Cross(piece.S, backS);
        EventBus.Instance.Log("The cart takes the switch onto the slag line.");
        EventBus.Instance.PlaySfx("switch_clunk"); // placeholder: plan 6 adds the sound
        GD.Print($"[rails] switch {piece.Plan.Id} taken at S={_rails.State.S:0.0}: across from {piece.S:0.0}, back from {backS:0.0}");
    }

    /// <summary>The heap ahead: its lantern blinks while a cart bears down on it, and a seated companion calls it once, while it can still be switched round.</summary>
    private void TickHeap(Piece piece, PartyController party, bool rolling)
    {
        if (piece.Heap is not { IsAlive: true } heap) return;
        var front = _rails.Party.FrontS;
        var headed = rolling && heap.FaceS >= front - 0.5f && !_rails.OnEnemyLane(heap.FaceS) && !SwitchSetBefore(piece);
        var look = Mathf.Max(_rails.State.V, RunOverRules.WarnMinSpeed) * WarnSeconds;
        heap.Alarm = headed && heap.FaceS - front <= look;
        if (piece.Barked || !headed) return;
        var aim = LeverAheadOf(piece, front) ?? heap.FaceS; // the call is useful while the lever is still ahead
        if (aim - front > look) return;
        piece.Barked = true;
        var by = Bark(party, "Line's blocked — switch!");
        EventBus.Instance.PlaySfx("cart_bell"); // placeholder: plan 6 adds the sound
        GD.Print($"[rails] heap {piece.Plan.Id} called at front S={front:0.0} ({heap.FaceS - front:0.0} m to the heap, aim {aim:0.0}); bark={by ?? "none"}");
    }

    /// <summary>A switch_out of the heap's ride before it, not yet taken, is thrown: the cart will go round.</summary>
    private bool SwitchSetBefore(Piece heap)
    {
        foreach (var p in _pieces)
            if (p.Plan.Ride == heap.Plan.Ride && p.Plan.Kind == "switch_out" && !p.Fired && p.S < heap.S && p.Switch is { Thrown: true }) return true;
        return false;
    }

    /// <summary>The lever of an untaken switch_out before the heap that the front has not reached yet, or null.</summary>
    private float? LeverAheadOf(Piece heap, float front)
    {
        foreach (var p in _pieces)
            if (p.Plan.Ride == heap.Plan.Ride && p.Plan.Kind == "switch_out" && !p.Fired && p.S < heap.S && p.S - LeverLead > front) return p.S - LeverLead;
        return null;
    }

    /// <summary>The buckets tip; a live patch the cart's body is over dents the plating and burns each seated hero, once per patch.</summary>
    private void TickBuckets(Piece piece, float dt)
    {
        if (piece.Buckets is not { } buckets) return;
        buckets.Tick(dt);
        if (_rails.State.Mode == CartMode.Wrecked) return;
        foreach (var patch in buckets.Patches)
        {
            if (patch.HitCart || !IsInstanceValid(patch)) continue;
            if (Mathf.Abs(patch.S - _rails.State.S) > CartRules.HalfLength + CinderPatch.Radius || _rails.OnEnemyLane(patch.S)) continue;
            patch.HitCart = true;
            EventBus.Instance.Log("The cart rolls through the cinders!");
            _burnt.Clear();
            foreach (var rider in _rails.Cart.Riders.Values) _burnt.Add(rider); // a burn can unseat: read the book first
            var attack = TrapRules.FloorFire(_builder.CurrentFloor);
            var burnt = 0;
            foreach (var rider in _burnt)
                if (rider is Adventurer { Downed: false } hero && IsInstanceValid(hero)) { hero.ReceiveAttack(Trap.TrapStats, attack, "A cinder"); burnt++; }
            GD.Print($"[rails] cinders {patch.Name} at S={patch.S:0.0} hit the cart at S={_rails.State.S:0.0}: plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - PatchDent):0}, {burnt} riders burnt");
            _rails.DentPlating(PatchDent);
            if (_rails.State.Mode == CartMode.Wrecked) return;
        }
    }

    /// <summary>
    /// The party's cart on the enemy lane runs into <paramref name="cart"/> from behind: the slag cart derails (its riders under
    /// it), the plating takes <see cref="RamDent"/> and the riders feel it. True when it was rammed.
    /// </summary>
    private bool Ram(SlagCart cart)
    {
        if (_rails.State.Mode != CartMode.Rolling || !_rails.FrontOnEnemyLane) return false;
        if (cart.Run.State.S < Lanes.EnemyS(_rails.State.S)) return false; // behind the party's cart
        var front = Lanes.EnemyS(_rails.Party.FrontS);
        if (cart.Run.BackS - front > RamReach) return false;
        Rams++;
        GD.Print($"[rails] t={_now:0.00} the party's cart rams {cart.Name} (party S={_rails.State.S:0.0} front on the enemy lane {front:0.0}, slag back {cart.Run.BackS:0.0}, v {_rails.State.V:0.0} vs {cart.Run.State.V:0.0}); plating {_rails.State.Plating:0} -> {Mathf.Max(0f, _rails.State.Plating - RamDent):0}");
        EventBus.Instance.Log("The cart rams the slag cart!");
        cart.Derail();
        foreach (var rider in _rails.Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.09f, 0.5f);
        _rails.DentPlating(RamDent);
        return true;
    }

    /// <summary>A seated companion says <paramref name="text"/> (bark, log, HUD); else the log. Returns who said it, or null.</summary>
    private string? Bark(PartyController party, string text)
    {
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Cart != _rails.Cart || hero.Control != ControlKind.Companion || hero.Downed) continue;
            var name = hero.State.Name;
            EventBus.Instance.Log($"{name}: \"{text}\"");
            EventBus.Instance.RaiseBark(name, new Line(text));
            Main.Instance?.Hud?.ShowBark(name, text, 2.5f);
            return name;
        }
        EventBus.Instance.Log(text);
        return null;
    }
}
