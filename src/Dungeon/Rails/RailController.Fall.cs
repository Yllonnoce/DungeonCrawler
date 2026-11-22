using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The train over a crevasse (plan 10): a missed jump or a fallen trestle there is a fall (<see cref="Fall"/>), which takes every
/// rider, every coupled wagon and its riders and the hounds aboard into the crevasse (<see cref="CrevasseWatch.Take"/>), loses the
/// wagons and plays the plunge; and the fallen's return (ruling 8): at the cart's arrival at a stop, or when a living hero on foot
/// steps onto a stop's platform (<see cref="FallenReturn"/>), each beside the cart or that hero. Server and solo; a client plays the
/// plunge from the cart state (<see cref="CartStateDto.Fell"/>).
/// </summary>
public partial class RailController
{
    // ---- a fall into a crevasse (plan 10) --------------------------------------------------------

    /// <summary>The last wreck was a fall (<see cref="Fall"/>): true from it until the next wreck (a plain one clears it) or a <see cref="Restore"/>. Clients see it as <see cref="CartStateDto.Fell"/>.</summary>
    public bool LastWreckFell { get; private set; }
    /// <summary>Falls so far (the server's).</summary>
    public int Falls { get; private set; }
    /// <summary>Where the train last fell (the cart's body's place), or null before any fall.</summary>
    public Vector3? FellAt { get; private set; }
    /// <summary>The last fall's toll: the riders the crevasse took (the cart's and the wagons'), and the wagons lost.</summary>
    private int _fellRiders, _fellWagons;

    /// <summary>
    /// A train over a crevasse that misses its jump or loses its trestle under it falls (server only; a no-op while wrecked): <paramref name="why"/>
    /// is logged, then the wreck as a fall (<see cref="Wreck"/>). Lethality changes none of it. Its events are told before this returns
    /// (a trestle's fall is judged in the encounter controller's tick): the fall is heard before anything that follows it in the frame.
    /// </summary>
    public void Fall(string why, bool seen = false)
    {
        FallNow(why, seen);
        Flush();
    }

    /// <summary>
    /// The fall itself (<see cref="Fall"/>); this controller's own tick calls it and tells the events at its end. <paramref name="seen"/>
    /// (the cavern bridge's take): the train's look drops through the gone cells with its riders' (<see cref="SeenFall"/>) and the wipe
    /// waits <see cref="BridgeRules.FallSeen"/> for it; the toll is the same.
    /// </summary>
    private void FallNow(string why, bool seen = false)
    {
        if (NetSession.IsClient || State.Mode == CartMode.Wrecked) return;
        if (CrevasseWatch.Instance is not { } watch || !IsInstanceValid(watch))
        {
            GD.PushError($"[rails] ERROR: the train would fall ({why}) on a floor with no CrevasseWatch: wrecked instead");
            Wreck();
            return;
        }
        var s = State.S;
        EventBus.Instance.Log(why);
        Falls++;
        FellAt = Cart.GlobalPosition;
        Props.Tremor.ShakeOnly(0.05f, 0.8f);
        if (seen) Main.Instance?.Party?.HoldWipe(BridgeRules.FallSeen); // before the toll: the riders fall now, the dungeon shifts after the fall is seen
        Wreck(fall: true, seen);
        GD.Print($"[rails] the train falls at S={s:0.0} ({why}): {_fellRiders} rider(s), {_fellWagons} wagon(s){(seen ? $", seen for {BridgeRules.FallSeen:0.0} s" : "")}");
    }

    /// <summary>
    /// The fall's toll (inside <see cref="Wreck"/>): every rider of the cart and of each coupled wagon, and every hound held at a boarder
    /// spot on either, is taken by the crevasse over the drop nearest the cart or the wagon (<see cref="OverTheDrop"/>); then the train is uncoupled and each of its wagons
    /// is lost (<see cref="CageWagon.SetLost"/>: hidden, not solid, saved in <c>ObjectiveStateDto.LostCages</c>).
    /// </summary>
    private void TakeTheTrain(List<CageWagon> wagons)
    {
        var watch = CrevasseWatch.Instance!;
        var at = OverTheDrop(Cart.GlobalPosition, Cart.GlobalPosition); // the cart is over a crevasse: a drop is always near it
        _fellRiders = 0;
        _fellWagons = wagons.Count;
        foreach (var rider in Aboard())
        {
            if (rider is Adventurer or Npc) watch.Take((Node3D)rider, at);
            else GetOut(rider); // nothing else rides the bench; were it to, it steps out at the lip
            _fellRiders++;
        }
        TakeBoarders(watch, Cart.Boarders, Cart, at);
        foreach (var wagon in wagons)
        {
            if (!IsInstanceValid(wagon)) continue;
            var wagonAt = OverTheDrop(wagon.GlobalPosition, at); // a wagon far back on a straight run: the cart's drop
            _fallRiders.Clear();
            _fallRiders.AddRange(wagon.Riders.Values);
            foreach (var rider in _fallRiders)
            {
                if (rider is GodotObject o && !IsInstanceValid(o)) continue;
                if (rider is Adventurer or Npc) watch.Take((Node3D)rider, wagonAt);
                else rider.GetOut(wagonAt, Vector3.Zero);
                _fellRiders++;
            }
            _fallRiders.Clear();
            TakeBoarders(watch, wagon.Boarders, wagon, wagonAt);
        }
        _train.UncoupleAll();
        foreach (var wagon in wagons)
            if (IsInstanceValid(wagon)) wagon.SetLost(true);
    }
    private readonly List<ICartRider> _fallRiders = new();

    /// <summary>The hounds landed on <paramref name="body"/>'s boarder spots go with it (the crevasse kills an enemy: no loot).</summary>
    private static void TakeBoarders(CrevasseWatch watch, Enemies.Enemy?[] spots, Node3D body, Vector3 at)
    {
        foreach (var hound in spots)
            if (hound != null && IsInstanceValid(hound) && !hound.IsQueuedForDeletion() && hound.IsAlive && hound.Carrier == body) watch.Take(hound, at);
    }

    /// <summary>Heroes in the world and on their feet (not down, not fallen, not parked). No allocation.</summary>
    private static int StandingHeroes()
    {
        var standing = 0;
        if (Main.Instance?.Party is { } party)
            foreach (var hero in party.Adventurers)
                if (IsInstanceValid(hero) && !hero.Parked && !hero.Downed) standing++;
        return standing;
    }

    private const float PlungeSeconds = 4f;
    /// <summary>How far round the cart (cells) the plunge looks for the drop to throw the train toward.</summary>
    private const int PlungeReach = 4;

    /// <summary>
    /// The train's look going over (every peer; the server's fall, a client's Wrecked edge with Fell): the cart's body (with
    /// <paramref name="cart"/>) and each of <paramref name="wagons"/>' copied onto a rigid body that nothing holds (layer 0, mask 0),
    /// thrown along the way it was going, toward the nearest chasm cell and down, freed after <see cref="PlungeSeconds"/>. Cosmetic and
    /// silent (the wreck's cue plays beside it).
    /// </summary>
    private void Plunge(IReadOnlyList<CageWagon> wagons, bool cart)
    {
        if (!IsInsideTree() || GetParent() is not Node3D root) return;
        var toward = TowardTheDrop(Cart.GlobalPosition);
        if (cart && Cart.GetNodeOrNull<Node3D>("Body") is { } body) PlungeBody(root, "CartPlunge", body, Cart.GlobalTransform, Cart.Velocity, toward);
        foreach (var wagon in wagons)
            if (IsInstanceValid(wagon) && wagon.GetNodeOrNull<Node3D>("Body") is { } wagonBody)
                PlungeBody(root, "WagonPlunge", wagonBody, wagon.GlobalTransform, wagon.Velocity, TowardTheDrop(wagon.GlobalPosition));
    }

    /// <summary>
    /// The cavern bridge's take seen (every peer; the server's fall, a client's Wrecked edge with Fell over the bridge's span): the cart's body
    /// (with <paramref name="cart"/>), each of <paramref name="wagons"/>' and the look of each hero still seated in the cart (<see cref="Adventurer.FallLook"/>)
    /// copied onto rigid bodies that nothing holds (layer 0, mask 0), dropping straight down through the gone cells with a little of the
    /// train's way on them, freed after <see cref="PlungeSeconds"/>; and this peer's player, if they rode it (<paramref name="localRode"/>), sees it from their seat as it
    /// goes (<see cref="FallEye"/>) for <see cref="BridgeRules.FallSeen"/>. Call it before the riders are taken. Cosmetic and silent.
    /// </summary>
    private void SeenFall(IReadOnlyList<CageWagon> wagons, bool cart, bool localRode)
    {
        if (!IsInsideTree() || GetParent() is not Node3D root) return;
        RigidBody3D? carFall = null;
        if (cart && Cart.GetNodeOrNull<Node3D>("Body") is { } body) carFall = DropBody(root, "CartDrop", body, Cart.GlobalTransform, Cart.Velocity);
        foreach (var wagon in wagons)
            if (IsInstanceValid(wagon) && wagon.GetNodeOrNull<Node3D>("Body") is { } wagonBody)
                DropBody(root, "WagonDrop", wagonBody, wagon.GlobalTransform, wagon.Velocity);
        if (carFall == null) return;
        foreach (var rider in Aboard())
            if (rider is Adventurer hero && IsInstanceValid(hero))
            {
                var look = hero.FallLook();
                var at = look.Transform;
                carFall.AddChild(look);
                look.GlobalTransform = at;
            }
        if (localRode && Main.Instance?.Party?.Local is { } local && IsInstanceValid(local)) FallEye(carFall, local.Camera);
    }

    /// <summary>The seen fall's eye lets go this long (ms) before the held wipe is judged, so the local camera is current when the floor goes.</summary>
    private const ulong FallEyeLead = 50;

    /// <summary>
    /// <paramref name="eye"/>'s view rides down with <paramref name="falling"/>: a camera like it, at its place, a child of the falling body,
    /// kept current each frame (a downed player's control passes on as the riders fall) until <see cref="FallEyeLead"/> before the held wipe
    /// is judged (<see cref="PartyController.WipeHeldUntil"/>; a client, with no wipe of its own to hold, for <see cref="BridgeRules.FallSeen"/>).
    /// Then the party's local camera is current again and the eye is freed; if the eye went first (freed with the floor the wipe rebuilt,
    /// or a client's floor change), the local camera is handed back on the rebuilt floor (Task 6 review I1). Every role.
    /// </summary>
    private void FallEye(RigidBody3D falling, Camera3D eye)
    {
        var cam = new Camera3D
        {
            Name = "FallEye", Fov = eye.Fov, Near = eye.Near, Far = eye.Far, CullMask = eye.CullMask, Environment = eye.Environment,
            Attributes = eye.Attributes, KeepAspect = eye.KeepAspect,
        };
        var at = eye.GlobalTransform;
        falling.AddChild(cam);
        cam.GlobalTransform = at;
        cam.MakeCurrent();
        var now0 = Time.GetTicksMsec();
        var held = Main.Instance?.Party?.WipeHeldUntil ?? 0UL;
        var until = held > now0 ? held - Math.Min(FallEyeLead, held - now0) : now0 + (ulong)(BridgeRules.FallSeen * 1000f); // one clock with the wipe's
        var tree = GetTree();
        Action? hold = null; // one delegate, so the same one is let go
        hold = () =>
        {
            var live = IsInstanceValid(cam);
            if (live && Time.GetTicksMsec() < until)
            {
                if (!cam.Current) cam.MakeCurrent();
                return;
            }
            tree.ProcessFrame -= hold;
            HandBackView(tree); // before the eye goes: the viewport never falls back on another hero's camera
            if (live) cam.QueueFree();
        };
        tree.ProcessFrame += hold;
    }

    /// <summary>The party's local hero's camera is made current unless it already is (a headset's rig takes the view back each frame itself).</summary>
    private static void HandBackView(SceneTree tree)
    {
        if (Main.Instance?.Party?.Local is not { } local || !IsInstanceValid(local) || !local.Camera.IsInsideTree()) return;
        if (tree.Root.GetCamera3D() != local.Camera) local.Camera.MakeCurrent();
    }

    /// <summary>One look of the seen fall: <paramref name="body"/> copied onto a rigid body at <paramref name="at"/>, dropping with a third of <paramref name="velocity"/>.</summary>
    private RigidBody3D DropBody(Node3D root, string name, Node3D body, Transform3D at, Vector3 velocity)
    {
        var falling = new RigidBody3D { Name = name, CollisionLayer = 0, CollisionMask = 0, Mass = 300f };
        falling.AddChild(body.Duplicate());
        root.AddChild(falling);
        falling.GlobalTransform = at;
        falling.LinearVelocity = velocity * 0.35f + Vector3.Down * 0.5f;
        falling.AngularVelocity = at.Basis.X * 0.6f; // the nose dips as the deck goes from under it
        GetTree().CreateTimer(PlungeSeconds).Timeout += () => { if (IsInstanceValid(falling)) falling.QueueFree(); };
        return falling;
    }

    private void PlungeBody(Node3D root, string name, Node3D body, Transform3D at, Vector3 velocity, Vector3 toward)
    {
        var falling = new RigidBody3D { Name = name, CollisionLayer = 0, CollisionMask = 0, Mass = 300f };
        falling.AddChild(body.Duplicate());
        root.AddChild(falling);
        falling.GlobalTransform = at;
        falling.LinearVelocity = velocity * 0.6f + toward * 2.5f + Vector3.Down * 1.5f;
        falling.AngularVelocity = at.Basis.Z * 1.2f + toward.Cross(Vector3.Up) * 1.5f;
        GetTree().CreateTimer(PlungeSeconds).Timeout += () => { if (IsInstanceValid(falling)) falling.QueueFree(); };
    }

    /// <summary>
    /// Where the crevasse takes a body riding at <paramref name="from"/>: the middle of the nearest drop cell (a chasm or a bridge,
    /// <see cref="CrevasseRules.IsDrop"/>) within <see cref="PlungeReach"/>, so the held and the dead lie under a drop (a load and a
    /// client's copy judge a dead body there as gone into the dark: <c>Npc.InTheDark</c>), not under the lane's solid floor beside it;
    /// <paramref name="fallback"/> when none is near (a wagon far back from the drop: the cart's own drop point).
    /// </summary>
    private Vector3 OverTheDrop(Vector3 from, Vector3 fallback)
    {
        if (_layout == null || _builder == null) return fallback;
        var here = DungeonBuilder.WorldToCell(from);
        if (CrevasseRules.IsDrop(_layout, here)) return from;
        GridPos? best = null;
        var bestD = int.MaxValue;
        for (var dy = -PlungeReach; dy <= PlungeReach; dy++)
            for (var dx = -PlungeReach; dx <= PlungeReach; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                var d = dx * dx + dy * dy;
                if (d < bestD && CrevasseRules.IsDrop(_layout, c)) { best = c; bestD = d; }
            }
        return best is { } cell ? _builder.Surface(cell) : fallback;
    }

    /// <summary>The flat unit direction from <paramref name="from"/> to the nearest chasm cell within <see cref="PlungeReach"/>, or zero.</summary>
    private Vector3 TowardTheDrop(Vector3 from)
    {
        if (_layout == null || _builder == null) return Vector3.Zero;
        var here = DungeonBuilder.WorldToCell(from);
        GridPos? best = null;
        var bestD = int.MaxValue;
        for (var dy = -PlungeReach; dy <= PlungeReach; dy++)
            for (var dx = -PlungeReach; dx <= PlungeReach; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                var d = dx * dx + dy * dy;
                if (d < bestD && _layout.IsChasm(c)) { best = c; bestD = d; }
            }
        if (best is not { } cell) return Vector3.Zero;
        var flat = _builder.Surface(cell) - from;
        flat.Y = 0f;
        return flat.LengthSquared() > 1e-4f ? flat.Normalized() : Vector3.Zero;
    }

    // ---- the fallen come back (ruling 8) ---------------------------------------------------------

    private readonly FallenReturn _fallenReturn = new();
    /// <summary>The stop the cart rolled into this tick (<see cref="Arrive"/>), or −1: the fallen come back there.</summary>
    private int _arrivedStop = -1;
    /// <summary>A hero was fallen at the last tick (the baseline is taken when the first falls).</summary>
    private bool _anyFallen;
    private readonly List<int> _fallenBaseline = new();
    private readonly List<(bool Living, bool Seated, int PlatformStop)> _fallenRows = new();

    /// <summary>Fallen returns so far (test hook).</summary>
    internal int FallenReturns { get; private set; }

    /// <summary>
    /// Each server or solo tick: while any hero is fallen, <see cref="FallenReturn"/> says when they come back: the cart rolled into a
    /// stop (each beside the cart), or a living hero on foot stepped onto a stop's platform after the fall (each beside that hero).
    /// No allocation.
    /// </summary>
    private void TickFallen(PartyController party)
    {
        var arrived = _arrivedStop;
        _arrivedStop = -1;
        var any = false;
        foreach (var hero in party.Adventurers)
            if (IsInstanceValid(hero) && hero.State.Fallen) { any = true; break; }
        if (!any) { _anyFallen = false; return; }
        if (!_anyFallen)
        {
            _anyFallen = true;
            _fallenBaseline.Clear();
            foreach (var hero in party.Adventurers) _fallenBaseline.Add(IsInstanceValid(hero) && !hero.Parked && !hero.Seated ? PlatformStopOf(hero.GlobalPosition) : -1);
            _fallenReturn.Baseline(_fallenBaseline);
        }
        _fallenRows.Clear();
        foreach (var hero in party.Adventurers)
        {
            var valid = IsInstanceValid(hero) && !hero.Parked;
            _fallenRows.Add((valid && !hero.Downed, valid && hero.Seated, valid ? PlatformStopOf(hero.GlobalPosition) : -1));
        }
        var stop = _fallenReturn.Tick(_fallenRows, arrived);
        if (stop < 0) return;
        var by = _fallenReturn.Hero;
        if (by >= 0 && by < party.Adventurers.Length && IsInstanceValid(party.Adventurers[by]))
        {
            var lead = party.Adventurers[by];
            ReturnFallen(party, lead.GlobalPosition, $"{lead.State.Name} stepped onto stop {stop}'s platform", DungeonBuilder.WorldToCell(lead.GlobalPosition));
        }
        else ReturnFallen(party, Cart.GlobalPosition, $"the cart rolled into stop {stop}");
    }

    /// <summary>The stop whose room <paramref name="at"/> stands in (its platform), or −1 off every stop.</summary>
    private int PlatformStopOf(Vector3 at)
    {
        if (_layout?.Rail is not { } rail) return -1;
        var cell = DungeonBuilder.WorldToCell(at);
        for (var i = 0; i < rail.Stops.Count; i++)
        {
            var room = rail.Stops[i].Room;
            if (room >= 0 && room < _layout.Rooms.Count && _layout.Rooms[room].Contains(cell)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Every fallen hero in the world climbs back out (<see cref="Adventurer.ReturnFromCrevasse"/>: a quarter of their HP), each on its
    /// own cell beside the lane near <paramref name="near"/> (<see cref="ExitCellNear"/>: never a drop, a rail or a siding), never on
    /// <paramref name="standing"/> (the cell of the hero they come back beside). Server and solo.
    /// </summary>
    private void ReturnFallen(PartyController party, Vector3 near, string why, GridPos? standing = null)
    {
        if (NetSession.IsClient || _layout?.Rail == null) return;
        _exitTaken.Clear();
        if (standing is { } own) _exitTaken.Add(own); // beside the hero who stepped on, not on top of them
        foreach (var hero in party.Adventurers)
        {
            if (!IsInstanceValid(hero) || hero.Parked || !hero.State.Fallen) continue;
            var cell = ExitCellNear(near, atStop: true, useTaken: true);
            _exitTaken.Add(cell);
            hero.ReturnFromCrevasse(_builder.Surface(cell) + Vector3.Up * 0.3f);
            FallenReturns++;
            GD.Print($"[rails] {hero.State.Name} comes back out of the crevasse at {cell} ({why})");
        }
        _exitTaken.Clear();
        _anyFallen = false; // a hero who falls after this is a new fall, with its own baseline
        RefreshHud();
    }
}
