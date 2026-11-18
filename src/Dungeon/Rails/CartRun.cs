using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// One cart running on a track: its <see cref="CartState"/>, the body it poses, which way it runs and where it stops.
/// The owner (the party's RailController; later spare carts, cage wagons and slag carts) builds the input and decides
/// what a stop or a hit means; this steps <see cref="CartPhysics"/> at a fixed 1/60 s (<see cref="CartClock"/>) and puts
/// the body on the line, evenly at any physics rate.
/// </summary>
public sealed class CartRun
{
    /// <summary><see cref="CartPhysics"/> steps exactly this long, whatever the caller's dt.</summary>
    public const float FixedStep = CartClock.FixedStep;
    /// <summary>At most this many fixed steps per <see cref="Step"/>; a longer stall drops the rest rather than catch up.</summary>
    public const int MaxSubSteps = CartClock.MaxSubSteps;

    public readonly TrackSampler Track;
    /// <summary>What <see cref="Pose"/> moves: the cart's node (−Z along its way of travel).</summary>
    public readonly Node3D Body;
    public CartState State;
    /// <summary>S before the last <see cref="Step"/> call: the run-over sweep spans PrevS to S.</summary>
    public float PrevS;
    /// <summary>S before the last fixed step taken (it stays put through a tick with no step): the rendered pose runs from it to S.</summary>
    public float PoseFrom;
    /// <summary>The input the last <see cref="Step"/> ran on (its StopAt is <see cref="Limit"/>).</summary>
    public CartInput LastInput;
    /// <summary>+1 runs toward higher S, −1 toward lower S.</summary>
    public int Dir = 1;
    /// <summary>The S the cart halts at (the input's StopAt): set it ahead of S, in <see cref="Dir"/>, before the cart rolls.</summary>
    public float Limit;

    private CartClock _clock;
    private float _tickDt = CartClock.FixedStep;

    public CartRun(TrackSampler track, Node3D body, CartState state, int dir = 1)
    {
        Track = track;
        Body = body;
        Dir = dir >= 0 ? 1 : -1;
        Reset(state);
    }

    /// <summary>The front end's S: the end that leads in <see cref="Dir"/>.</summary>
    public float FrontS => State.S + Dir * CartRules.HalfLength;

    /// <summary>The trailing end's S.</summary>
    public float BackS => State.S - Dir * CartRules.HalfLength;

    /// <summary>S moved in <see cref="Dir"/> over the last step.</summary>
    public bool Moving => Dir >= 0 ? State.S > PrevS : State.S < PrevS;

    /// <summary>Rise over run at S in the way of travel: the Slope a <see cref="CartInput"/> wants.</summary>
    public float SlopeAhead => Dir * Track.Slope(State.S);

    /// <summary>Put the cart in <paramref name="state"/> with nothing carried over (PrevS = PoseFrom = S, no partial step).</summary>
    public void Reset(CartState state)
    {
        State = state;
        LastInput = default;
        Settle();
    }

    /// <summary>The cart stands where it is: nothing to sweep or render between, no time banked (each tick it does not roll, and as it departs).</summary>
    public void Settle()
    {
        PrevS = PoseFrom = State.S;
        _clock.Reset();
    }

    /// <summary>
    /// Advance by the physics tick <paramref name="dt"/>: as many 1/60 s steps of <see cref="CartPhysics"/> as have built up
    /// (at most <see cref="MaxSubSteps"/>), every one on <paramref name="input"/> with <see cref="Limit"/> as its StopAt. The
    /// input's Slope is in the way of travel (<see cref="SlopeAhead"/>). PrevS is S before the first of them, PoseFrom S
    /// before the last. A cart running toward lower S is stepped mirrored about the track's end, so CartPhysics only ever
    /// sees a forward cart.
    /// </summary>
    public void Step(in CartInput input, float dt)
    {
        PrevS = State.S;
        LastInput = input with { StopAt = Limit };
        _tickDt = dt;
        var steps = _clock.Advance(dt);
        if (steps == 0) return;

        var length = Track.Length;
        var forward = Dir >= 0;
        var i = forward ? LastInput : LastInput with { StopAt = length - Limit };
        var c = forward ? State : State with { S = length - State.S };
        var from = c.S;
        for (var n = 0; n < steps; n++) { from = c.S; c = CartPhysics.Step(c, i, FixedStep); }
        PoseFrom = forward ? from : length - from;
        State = forward ? c : c with { S = length - c.S };
    }

    /// <summary>
    /// The S to show <paramref name="frac"/> (the physics interpolation fraction) of the way through the current tick: between
    /// PoseFrom and S by the time banked plus the tick's elapsed share, so the cart moves evenly when the physics rate is not
    /// 60 Hz (a headset's rate). At 60 Hz it is the plain lerp from PrevS by frac.
    /// </summary>
    public float RenderS(float frac) => CartClock.RenderS(PoseFrom, State.S, _clock.Alpha(frac, _tickDt));

    /// <summary>Pose the body at <see cref="RenderS"/>.</summary>
    public void Pose(float frac) => PoseAt(RenderS(frac));

    /// <summary>Put the body on the line at <paramref name="s"/>, facing its way of travel.</summary>
    public void PoseAt(float s)
    {
        if (!GodotObject.IsInstanceValid(Body) || !Track.Valid) return;
        var frame = Track.Frame(s);
        var basis = Dir >= 0 ? frame.Basis : frame.Basis * new Basis(Vector3.Up, Mathf.Pi);
        Body.GlobalTransform = new Transform3D(basis.Orthonormalized(), frame.Origin);
    }

    /// <summary>A cart body (<see cref="ICartBody"/>) learns its speed and the bend it is in (its riders' ride feel and velocity).</summary>
    public void Sync()
    {
        if (Body is not ICartBody cart || !GodotObject.IsInstanceValid(Body)) return;
        cart.Speed = State.V;
        cart.Curvature = Dir * Track.Curvature(State.S); // a reversing cart reads the bend behind its back end: close enough for the lean
    }
}

/// <summary>What happened to a cart, for whoever listens (plan 5 syncs it to clients).</summary>
public enum CartEventKind { Departed, Arrived, Hit, Wrecked, Seated, Unseated, Restored }

/// <param name="Stop">The stop concerned (Departed, Wrecked: the one last left; Arrived, Restored: the one reached), or −1.</param>
/// <param name="Rider">The rider's <see cref="ICartRider.RiderId"/> (Seated, Unseated), or −1.</param>
/// <param name="Target">The instance id of the body run over (Hit), or 0.</param>
/// <param name="Thrown">Unseated from a moving cart at speed.</param>
public readonly record struct CartEvent(CartEventKind Kind, int Stop = -1, int Rider = -1, ulong Target = 0, bool Thrown = false);
