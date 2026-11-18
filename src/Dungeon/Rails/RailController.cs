using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Runs the party's mine cart on a railed floor: a node under the floor root (it dies with the floor). It seats and
/// unseats heroes, counts down the boarding, steps <see cref="CartPhysics"/> from stop to stop on the server (or solo),
/// and poses the cart and its riders every rendered frame. A client only shows the cart parked at the first stop.
/// </summary>
public partial class RailController : Node
{
    public static RailController? Instance { get; private set; }

    /// <summary>A hero mangled by the cart carries this as MangledBy: no hazard has it, so it always counts as armed.</summary>
    public const int HazardIndex = 1000;
    /// <summary>Companions this close to the human who boards hop in with them.</summary>
    public const float CompanionBoardRange = 25f;
    /// <summary>The cart's HUD line shows while a hero is seated or this close.</summary>
    public const float HudRange = 12f;
    const float HudEvery = 0.25f;
    const float CurvatureSpan = 2f;
    const string SourceName = "the warden cart";

    public MineCart Cart { get; private set; } = null!;
    public CartState State { get; private set; }
    /// <summary>The stop the cart is at, or last left.</summary>
    public int StopIndex { get; private set; }
    /// <summary>Each stop's distance along the party track, ascending.</summary>
    public float[] StopS { get; private set; } = Array.Empty<float>();
    /// <summary>The cart's status line, or null when it is not shown.</summary>
    public string? HudText { get; private set; }
    public Boarding Boarding { get; private set; } = new();

    private RailTrack _track = null!;
    private FloorLayout _layout = null!;
    private DungeonBuilder _builder = null!;
    private float _prevS;
    private float _hudTimer;

    public RailController()
    {
        Name = "RailController";
        ProcessPriority = -100; // pose the cart before the heroes' own _Process (their cameras) runs
    }

    /// <summary>Read the stops off the track and park the cart at the first. Call before adding the controller to the tree.</summary>
    public void Setup(DungeonBuilder builder, FloorLayout layout, RailTrack track, Node3D root)
    {
        _builder = builder;
        _layout = layout;
        _track = track;
        var stops = new List<float>();
        for (var i = 0; i < (layout.Rail?.Stops.Count ?? 0); i++)
            if (root.GetNodeOrNull<Node3D>($"RailStop{i}") is { } marker)
                stops.Add(track.Curve.GetClosestOffset(track.ToLocal(marker.GlobalPosition)));
        stops.Sort();
        StopS = stops.ToArray();
        State = new CartState(StopS.Length > 0 ? StopS[0] : 0f, 0f, CartMode.Held, CartRules.MaxPlating);
        _prevS = State.S;
        Cart = new MineCart { PromptFor = PromptFor, OnUse = Use };
        root.AddChild(Cart);
    }

    public override void _EnterTree() => Instance = this;

    public override void _Ready() => PoseAt(State.S);

    public override void _ExitTree()
    {
        if (Instance != this) return;
        Instance = null;
        Main.Instance?.Hud?.SetCart(null, null);
    }

    /// <summary>The floor this controller belongs to is still the one built (a cleared floor waits a frame to be freed).</summary>
    private bool Current => Main.Instance is { InGame: true } main && main.Builder.PartyTrack == _track && IsInstanceValid(Cart);

    // ---- boarding --------------------------------------------------------------------------------

    private string PromptFor(Adventurer hero)
    {
        if (NetSession.IsClient || hero.Control == ControlKind.Remote) return ""; // co-op riding is plan 5
        if (hero.Cart == Cart) return "Get out";
        return State.Mode == CartMode.Held && !hero.Seated ? "Board the cart" : "";
    }

    private void Use(Adventurer actor)
    {
        if (NetSession.IsClient || actor.Control == ControlKind.Remote || !Current) return;
        if (actor.Cart == Cart) { GetOut(actor); return; }
        if (actor.Seated || actor.Downed || State.Mode != CartMode.Held) return;
        if (!SeatHero(actor)) return;
        if (actor.Control == ControlKind.LocalHuman)
            foreach (var mate in Main.Instance.Party.Active)
                if (mate != actor && mate.Control == ControlKind.Companion && !mate.Downed && !mate.Seated
                    && mate.GlobalPosition.DistanceTo(actor.GlobalPosition) <= CompanionBoardRange)
                    SeatHero(mate); // they hop in; walking to the cart is polish for later
        if (!Boarding.Running && StopIndex < StopS.Length - 1) Boarding.Start();
        RefreshHud();
    }

    private bool SeatHero(Adventurer hero)
    {
        if (Cart.Seats.Take(hero.Index, isHero: true) is not int seat) return false;
        hero.SitIn(Cart, seat);
        return true;
    }

    private void GetOut(Adventurer hero)
    {
        var velocity = Cart.Velocity;
        var thrown = State.V >= RunOverRules.ThrownAtSpeed;
        Cart.Seats.Leave(hero.Index);
        var at = _builder.Surface(ExitCell());
        if (thrown) EventBus.Instance.Log($"{hero.State.Name} jumps from the moving cart!");
        hero.GetOut(at, velocity);
        if (thrown) hero.ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(VictimKind.Member, false, TierRules.Lethality), SourceName, HazardIndex);
        RefreshHud();
    }

    /// <summary>Where a rider gets out: beside the lane at a stop, else on the lane cell under the cart.</summary>
    private GridPos ExitCell()
    {
        var lane = _layout.Rail!.Party;
        var here = DungeonBuilder.WorldToCell(Cart.GlobalPosition);
        int best = 0, bestD = int.MaxValue;
        for (var i = 0; i < lane.Count; i++)
        {
            int dx = lane[i].X - here.X, dy = lane[i].Y - here.Y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return CartSeats.ExitCell(lane, best, _layout.IsWalkable, atStop: State.Mode == CartMode.Held);
    }

    // ---- the ride --------------------------------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (NetSession.IsClient || !Current) return;
        var dt = (float)delta;
        var party = Main.Instance.Party;
        _prevS = State.S;

        if (State.Mode == CartMode.Held)
        {
            var seated = SeatedLiving(party);
            if (seated == 0) Boarding = new Boarding(); // nobody standing aboard: the countdown stops, the cart does not leave empty (or with only the fallen)
            else if (Boarding.Tick(dt, seated, party.Active.Count(a => !a.Downed)) && StopIndex < StopS.Length - 1)
            {
                State = State with { Mode = CartMode.Rolling }; // StopAt (the next stop) is ahead of S before the cart rolls
                RefreshHud();
            }
        }

        if (State.Mode == CartMode.Rolling)
        {
            var slope = SlopeAt(State.S);
            var input = new CartInput(slope, false, 0, 0, StopS[StopIndex + 1]);
            input = Inputs(party, input);
            State = CartPhysics.Step(State, input, dt);
            if (State.Mode == CartMode.Held) Arrive(party);
        }

        Cart.Speed = State.V;
        Cart.Curvature = CurvatureAt(State.S);
    }

    /// <summary>The seated human brakes (back) or pumps (forward); seated companions pump on a climb or when slow, two pumpers at most.</summary>
    private CartInput Inputs(PartyController party, CartInput input)
    {
        bool brake = false;
        var pumpers = 0;
        if (party.Local is { } me && me.Cart == Cart && !me.Downed && me.Control == ControlKind.LocalHuman)
        {
            var move = me.Intent.Move; // forward is −Y (move_forward is the vector's negative Y)
            brake = move.Y > 0.5f;
            if (move.Y < -0.5f) pumpers = 1;
        }
        var companionsPump = input.Slope > CartRules.ClimbSlope || State.V < 0.7f * CartRules.Cruise;
        foreach (var hero in party.Active)
        {
            if (hero.Cart != Cart) continue;
            var pump = companionsPump && !brake && pumpers < 2 && hero.Control == ControlKind.Companion && !hero.Downed;
            if (pump) pumpers++;
            hero.Pumping = pump;
        }
        return input with { Brake = brake, Pumpers = pumpers };
    }

    private void Arrive(PartyController party)
    {
        StopIndex++;
        foreach (var hero in party.Active) if (hero.Cart == Cart) hero.Pumping = false;
        var last = StopIndex >= StopS.Length - 1;
        EventBus.Instance.Log(last ? "The cart reaches the end of the line." : $"The cart rolls into stop {StopIndex}.");
        if (!last && Cart.Seats.Count > 0) Boarding.Start(); // plan 3 adds the rail gate and the departure lever
        RefreshHud();
    }

    private int SeatedLiving(PartyController party) => party.Active.Count(a => a.Cart == Cart && !a.Downed);

    // ---- posing ----------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Current) return;
        var s = NetSession.IsClient ? State.S : Mathf.Lerp(_prevS, State.S, (float)Engine.GetPhysicsInterpolationFraction());
        PoseAt(s);
        foreach (var hero in Main.Instance.Party.Adventurers)
            if (IsInstanceValid(hero) && hero.Cart == Cart) hero.FollowSeat();
        if (NetSession.IsClient) return;
        _hudTimer -= (float)delta;
        if (_hudTimer <= 0f) RefreshHud();
    }

    private void PoseAt(float s)
    {
        if (!IsInstanceValid(Cart) || !IsInstanceValid(_track)) return;
        Cart.Pose(_track.GlobalTransform * _track.Curve.SampleBakedWithRotation(Mathf.Clamp(s, 0f, _track.Length), true, true));
    }

    private Vector3 TrackPoint(float s) => _track.ToGlobal(_track.Curve.SampleBaked(Mathf.Clamp(s, 0f, _track.Length), true));

    /// <summary>Rise over run at S: the track's height half a metre either side.</summary>
    private float SlopeAt(float s)
    {
        float a = Mathf.Max(0f, s - 0.5f), b = Mathf.Min(_track.Length, s + 0.5f);
        return b - a < 0.01f ? 0f : (TrackPoint(b).Y - TrackPoint(a).Y) / (b - a);
    }

    /// <summary>The signed change in flat heading over the next two metres, per metre (positive bends left).</summary>
    private float CurvatureAt(float s)
    {
        float a = Mathf.Clamp(s, 0f, _track.Length), b = Mathf.Min(_track.Length, a + CurvatureSpan);
        if (b - a < 0.01f) return 0f;
        return Mathf.Wrap(Heading(b) - Heading(a), -Mathf.Pi, Mathf.Pi) / (b - a);
    }

    private float Heading(float s)
    {
        var f = -(_track.GlobalBasis * _track.Curve.SampleBakedWithRotation(s, true, true).Basis).Z;
        return Mathf.Atan2(-f.X, -f.Z);
    }

    // ---- HUD -------------------------------------------------------------------------------------

    private void RefreshHud()
    {
        _hudTimer = HudEvery;
        var party = Main.Instance?.Party;
        var show = party != null && party.Active.Any(a => a.Cart == Cart || a.GlobalPosition.DistanceTo(Cart.GlobalPosition) <= HudRange);
        HudText = show ? Line() : null;
        Main.Instance?.Hud?.SetCart(show ? State.Plating / CartRules.MaxPlating : null, HudText);
    }

    private string Line()
    {
        if (State.Mode == CartMode.Wrecked) return "Cart — wrecked";
        if (Boarding.Running) return $"Cart — {Mathf.RoundToInt(State.Plating / CartRules.MaxPlating * 100f)}% — departs in {Mathf.CeilToInt(Boarding.Left)}";
        if (State.Mode == CartMode.Rolling) return "Cart — rolling";
        return $"Cart — at stop {StopIndex}";
    }
}
