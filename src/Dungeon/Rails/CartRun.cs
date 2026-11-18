using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// One cart running on a track: its <see cref="CartState"/>, the body it poses, which way it runs and where it stops.
/// The owner (the party's RailController; later spare carts, cage wagons and slag carts) builds the input and decides
/// what a stop or a hit means; this steps <see cref="CartPhysics"/> at a fixed 1/60 s and puts the body on the line.
/// </summary>
public sealed class CartRun
{
    /// <summary><see cref="CartPhysics"/> steps exactly this long, whatever the caller's dt.</summary>
    public const float FixedStep = 1f / 60f;
    /// <summary>At most this many fixed steps per <see cref="Step"/>; a longer stall drops the rest rather than catch up.</summary>
    public const int MaxSubSteps = 8;

    public readonly TrackSampler Track;
    /// <summary>What <see cref="Pose"/> moves: the cart's node (−Z along its way of travel).</summary>
    public readonly Node3D Body;
    public CartState State;
    /// <summary>S before the last <see cref="Step"/>: the run-over sweep and the rendered pose span PrevS to S.</summary>
    public float PrevS;
    /// <summary>The input the last <see cref="Step"/> ran on (its StopAt is <see cref="Limit"/>).</summary>
    public CartInput LastInput;
    /// <summary>+1 runs toward higher S, −1 toward lower S.</summary>
    public int Dir = 1;
    /// <summary>The S the cart halts at (the input's StopAt): set it ahead of S, in <see cref="Dir"/>, before the cart rolls.</summary>
    public float Limit;

    private float _accumulated;

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

    /// <summary>Put the cart in <paramref name="state"/> with nothing carried over (PrevS = S, no partial step).</summary>
    public void Reset(CartState state)
    {
        State = state;
        PrevS = state.S;
        _accumulated = 0f;
        LastInput = default;
    }

    /// <summary>
    /// Advance by <paramref name="dt"/>: as many 1/60 s steps of <see cref="CartPhysics"/> as have built up (at most
    /// <see cref="MaxSubSteps"/>), every one on <paramref name="input"/> with <see cref="Limit"/> as its StopAt. The input's
    /// Slope is in the way of travel (<see cref="SlopeAhead"/>). PrevS is S before the first of them. A cart running
    /// toward lower S is stepped mirrored about the track's end, so CartPhysics only ever sees a forward cart.
    /// </summary>
    public void Step(in CartInput input, float dt)
    {
        PrevS = State.S;
        LastInput = input with { StopAt = Limit };
        _accumulated += dt;
        var steps = (int)(_accumulated / FixedStep + 1e-4f);
        if (steps > MaxSubSteps) { steps = MaxSubSteps; _accumulated = 0f; }
        else _accumulated = Mathf.Max(0f, _accumulated - steps * FixedStep);
        if (steps == 0) return;

        var length = Track.Length;
        var forward = Dir >= 0;
        var i = forward ? LastInput : LastInput with { StopAt = length - Limit };
        var c = forward ? State : State with { S = length - State.S };
        for (var n = 0; n < steps; n++) c = CartPhysics.Step(c, i, FixedStep);
        State = forward ? c : c with { S = length - c.S };
    }

    /// <summary>Pose the body <paramref name="frac"/> of the way from PrevS to S (the physics interpolation fraction; 1 is S).</summary>
    public void Pose(float frac) => PoseAt(frac >= 1f ? State.S : Mathf.Lerp(PrevS, State.S, frac));

    /// <summary>Put the body on the line at <paramref name="s"/>, facing its way of travel.</summary>
    public void PoseAt(float s)
    {
        if (!GodotObject.IsInstanceValid(Body) || !Track.Valid) return;
        var frame = Track.Frame(s);
        var basis = Dir >= 0 ? frame.Basis : frame.Basis * new Basis(Vector3.Up, Mathf.Pi);
        Body.GlobalTransform = new Transform3D(basis.Orthonormalized(), frame.Origin);
    }

    /// <summary>A <see cref="MineCart"/> body learns its speed and the bend it is in (its riders' ride feel and velocity).</summary>
    public void Sync()
    {
        if (Body is not MineCart cart || !GodotObject.IsInstanceValid(cart)) return;
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
