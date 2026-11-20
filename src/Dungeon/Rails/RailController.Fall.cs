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
    public void Fall(string why)
    {
        FallNow(why);
        Flush();
    }

    /// <summary>The fall itself (<see cref="Fall"/>); this controller's own tick calls it and tells the events at its end.</summary>
    private void FallNow(string why)
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
        Wreck(fall: true);
        GD.Print($"[rails] the train falls at S={s:0.0} ({why}): {_fellRiders} rider(s), {_fellWagons} wagon(s)");
    }

    /// <summary>
    /// The fall's toll (inside <see cref="Wreck"/>): every rider of the cart and of each coupled wagon, and every hound held at a boarder
    /// spot on either, is taken by the crevasse over the drop nearest the cart or the wagon (<see cref="OverTheDrop"/>); then the train is uncoupled and each of its wagons
    /// is lost (<see cref="CageWagon.SetLost"/>: hidden, not solid, saved in <c>ObjectiveStateDto.LostCages</c>).
    /// </summary>
    private void TakeTheTrain(List<CageWagon> wagons)
    {
        var watch = CrevasseWatch.Instance!;
        var at = OverTheDrop(Cart.GlobalPosition);
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
            var wagonAt = OverTheDrop(wagon.GlobalPosition);
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
    /// <paramref name="from"/> itself when none is near (it is a drop then, or the caller is not over a crevasse).
    /// </summary>
    private Vector3 OverTheDrop(Vector3 from)
    {
        if (_layout == null || _builder == null) return from;
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
        return best is { } cell ? _builder.Surface(cell) : from;
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
