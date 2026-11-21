using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// The party's cart off its own lane, and the line blocked ahead of it. A thrown switch (<see cref="Cross"/>) slides the
/// cart's pose across to the enemy lane over <see cref="SwitchStretch"/> metres after the switch's S and back after the
/// return switch's: <see cref="LaneBlendAt"/> is read per S, so each coupled wagon crosses at its own S. While the front is
/// on the enemy lane the run-over sweep and the "cart coming" warning judge bodies against that lane. An obstacle on the
/// party lane (<see cref="BlockLine"/>, an ore heap) stops a cart that reaches it there: the plating takes the hit and the
/// cart is Held on the line (<see cref="Halted"/>) until the line is clear (<see cref="ClearBlock"/>), then boards and
/// departs to the next stop as at a stop (a <see cref="CartEventKind.Resumed"/> event, not a Departed one). Server only.
/// </summary>
public partial class RailController
{
    /// <summary>A switch eases the cart across between the lanes over this many metres.</summary>
    public const float SwitchStretch = 6f;

    private TrackSampler? _enemyLane;
    private LaneMap? _lanes;
    /// <summary>The cart judged on the enemy lane: the party run's state mapped across, the same body (its riders ride).</summary>
    private CartRun? _laneView;
    private Func<float, Transform3D>? _laneFrame;
    private float _crossOut = float.NaN, _crossBack = float.NaN;
    private Func<Vector3, Vector3>? _setAsideEnemy;

    /// <summary>The S on the party lane where the cart's front meets an obstacle (an ore heap's face); +∞ while the line is clear.</summary>
    public float BlockedAt { get; private set; } = float.PositiveInfinity;
    private float _blockDent;
    /// <summary>The cart is held on the line short of the next stop (it ran into an obstacle), not at <see cref="StopIndex"/>.</summary>
    public bool Halted { get; private set; }
    /// <summary>An obstacle stands on the party lane (a client reads the server's flag).</summary>
    private bool LineBlocked => NetSession.IsClient ? (_netFlags & (byte)CartFlags.LineBlocked) != 0 : !float.IsPositiveInfinity(BlockedAt);
    /// <summary>Nothing at the cart's place holds it: the gate of the stop it is held at is up, or it is halted on the line past that stop (its gate is behind it). A client reads the server's flag.</summary>
    private bool GateClear => NetSession.IsClient ? (_netFlags & (byte)CartFlags.GateClear) != 0 : Halted || GateUp(StopIndex);

    /// <summary>The enemy lane beside the party's and the map between them (the encounter controller has both). Before any <see cref="Cross"/>.</summary>
    public void UseLanes(TrackSampler enemy, LaneMap lanes)
    {
        _enemyLane = enemy;
        _lanes = lanes;
        _laneView = new CartRun(enemy, Cart, State);
        _laneFrame = LaneFrame;
        _setAsideEnemy = SetAsideBesideEnemyLane;
    }

    /// <summary>How far the cart posed at party S <paramref name="s"/> stands across toward the enemy lane: 0 its own lane, 1 the enemy's.</summary>
    public float LaneBlendAt(float s)
    {
        if (float.IsNaN(_crossOut) || _lanes == null) return 0f;
        var up = Smooth((s - _crossOut) / SwitchStretch);
        var down = float.IsNaN(_crossBack) ? 0f : Smooth((s - _crossBack) / SwitchStretch);
        return Mathf.Min(up, 1f - down);
    }

    /// <summary>The cart's middle, as it stands now, on the lane blend.</summary>
    public float LaneBlend => LaneBlendAt(State.S);

    /// <summary>At party S <paramref name="s"/> the cart counts as on the enemy lane (half way across or more).</summary>
    public bool OnEnemyLane(float s) => LaneBlendAt(s) >= 0.5f;

    /// <summary>The party's cart counts as on the enemy lane: its front is half way across or more. The one test for spawning, pacing, ramming and the sweep.</summary>
    public bool FrontOnEnemyLane => OnEnemyLane(Party.FrontS);

    /// <summary>The cart has taken a switch this ride: it is posed across the lanes by S.</summary>
    public bool Crossing => !float.IsNaN(_crossOut);

    /// <summary>The party S the cart crossed out at (NaN on its own lane), every role: a client's is the server's (<see cref="ApplyLaneNet"/>).</summary>
    public float CrossOutS => _crossOut;
    /// <summary>The party S the cart crosses back at (NaN: it stays across until the ride ends), every role.</summary>
    public float CrossBackS => _crossBack;

    /// <summary>Server test hook: while <see cref="Crossing"/>, the cart stays across on the slag line (its return is undone).</summary>
    internal void DebugStayAcross()
    {
        if (NetSession.IsClient || !Crossing) return;
        Cross(_crossOut, float.NaN);
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>
    /// The cart took a thrown switch at party S <paramref name="outS"/>: it eases onto the enemy lane after it and back after
    /// <paramref name="backS"/> (NaN: it stays across until the ride ends). Server only.
    /// </summary>
    public void Cross(float outS, float backS)
    {
        if (NetSession.IsClient || _laneFrame == null) return;
        _crossOut = outS;
        _crossBack = backS;
        RefreshFrameAt(); // one FrameAt for the lane blend and a jump's arc
    }

    /// <summary>
    /// Client: the lane blend the server's cart state names (<paramref name="outS"/> and <paramref name="backS"/> as <see cref="Cross"/>
    /// takes them, NaN for none). Sets no server state; the pose follows at once.
    /// </summary>
    internal void ApplyLaneNet(float outS, float backS)
    {
        if (!NetSession.IsClient || _laneFrame == null) return;
        if (Same(_crossOut, outS) && Same(_crossBack, backS)) return;
        _crossOut = outS;
        _crossBack = backS;
        RefreshFrameAt();
    }

    /// <summary>Two floats the same, NaN equal to NaN.</summary>
    private static bool Same(float a, float b) => float.IsNaN(a) ? float.IsNaN(b) : a == b;

    /// <summary>Back on the party lane for good (the ride is over or undone).</summary>
    public void ClearLane()
    {
        _crossOut = _crossBack = float.NaN;
        RefreshFrameAt();
    }

    /// <summary>
    /// The frame at party S <paramref name="s"/> across the lanes: the two lanes' frames (the enemy's at its mapped S) with the
    /// position lerped and the basis slerped by the blend, then turned to the path the blend draws (the cart steers across
    /// rather than sliding sideways). Track-forward and unflipped: the run applies its direction.
    /// </summary>
    private Transform3D LaneFrame(float s)
    {
        var party = Party.Track.Frame(s);
        var b = LaneBlendAt(s);
        if (b <= 0f || _enemyLane == null || _lanes == null) return party;
        var enemy = _enemyLane.Frame(_lanes.EnemyS(s));
        if (b >= 1f) return enemy;
        var origin = party.Origin.Lerp(enemy.Origin, b);
        var q = party.Basis.Orthonormalized().GetRotationQuaternion().Slerp(enemy.Basis.Orthonormalized().GetRotationQuaternion(), b);
        var basis = new Basis(q);
        const float h = 0.5f;
        var along = LanePoint(s + h) - LanePoint(s - h);
        if (along.LengthSquared() < 1e-4f) return new Transform3D(basis, origin);
        var z = -along.Normalized();
        var x = basis.Y.Cross(z);
        if (x.LengthSquared() < 1e-6f) return new Transform3D(basis, origin);
        x = x.Normalized();
        return new Transform3D(new Basis(x, z.Cross(x).Normalized(), z), origin);
    }

    /// <summary>The cart's place (no frame) at party S across the lanes.</summary>
    public Vector3 LanePoint(float s)
    {
        var p = Party.Track.Point(s);
        var b = LaneBlendAt(s);
        if (b <= 0f || _enemyLane == null || _lanes == null) return p;
        return p.Lerp(_enemyLane.Point(_lanes.EnemyS(s)), b);
    }

    /// <summary>The run the sweep and the warning judge: the party's own, or its view on the enemy lane while the front is across.</summary>
    private CartRun JudgedRun()
    {
        if (_laneView == null || _lanes == null || !FrontOnEnemyLane) return Party;
        _laneView.State = State with { S = _lanes.EnemyS(State.S) };
        _laneView.PrevS = _lanes.EnemyS(Party.PrevS);
        return _laneView;
    }

    /// <summary>Where a big body knocked aside on the judged lane lands.</summary>
    private Func<Vector3, Vector3> SetAsideFor(CartRun run) => run == _laneView && _setAsideEnemy != null ? _setAsideEnemy : _setAside;

    // ---- a blocked line --------------------------------------------------------------------------

    /// <summary>An obstacle's face stands on the party lane at <paramref name="faceS"/>: a cart reaching it there loses <paramref name="dent"/> plating and stops. Server only.</summary>
    public void BlockLine(float faceS, float dent)
    {
        if (NetSession.IsClient) return;
        BlockedAt = faceS;
        _blockDent = dent;
        RefreshHud();
    }

    /// <summary>The line is clear: a cart halted at the obstacle may board and go on (its boarding starts if a player sits in it).</summary>
    public void ClearBlock()
    {
        if (!LineBlocked) return;
        BlockedAt = float.PositiveInfinity;
        if (Halted && State.Mode == CartMode.Held && !Boarding.Running && PlayerAboard()) StartBoarding();
        RefreshHud(); // a Boarding event raised here is told at the end of the next physics tick
    }

    /// <summary>Right after a step: the front reached the obstacle on the party lane. The cart is put back at its face and the plating takes the hit (the caller wrecks or halts it; the halt is the cart's sound's crash).</summary>
    private bool HitBlock()
    {
        if (!LineBlocked || State.Mode != CartMode.Rolling) return false;
        // the front reached the face in this step, on the party lane there (a cart that went round on the enemy lane passed it long ago)
        if (Party.PrevS + CartRules.HalfLength >= BlockedAt || Party.FrontS < BlockedAt || OnEnemyLane(BlockedAt)) return false;
        var s = BlockedAt - CartRules.HalfLength;
        Party.State = State with { S = Mathf.Min(State.S, s), V = 0f, Plating = Mathf.Max(0f, State.Plating - _blockDent) };
        EventBus.Instance.Log("The cart crashes into the ore heap!");
        foreach (var rider in Cart.Riders.Values)
            if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Shake(0.12f, 0.7f);
        Props.Tremor.ShakeOnly(0.04f, 0.5f);
        return true;
    }

    /// <summary>Held on the line at the obstacle: nobody leaves until it is cleared.</summary>
    private void Halt()
    {
        Party.State = State with { Mode = CartMode.Held, V = 0f };
        Party.Settle();
        SetCartLayer();
        Cart.Speed = 0f;
        Halted = true;
        Boarding.Cancel();
        foreach (var rider in Cart.Riders.Values) if (rider is Adventurer hero && IsInstanceValid(hero)) hero.Pumping = false;
        EventBus.Instance.Log("The line is blocked. Clear the heap.");
        Raise(new CartEvent(CartEventKind.Halted, Stop: StopIndex));
        RefreshHud();
    }

    /// <summary>The cart is at a stop again, or undone: no halt, no lane, no obstacle, no gap (the encounter controller sets them again when it re-arms).</summary>
    private void ResetLine()
    {
        Halted = false;
        BlockedAt = float.PositiveInfinity;
        ClearGap(); // the encounter controller sets it again when the ride arms
        ClearLane();
    }
}
