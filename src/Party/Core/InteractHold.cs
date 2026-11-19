namespace DungeonCrawler.Party;

/// <summary>What letting go of interact does (<see cref="InteractHold.Release"/>).</summary>
public enum InteractRelease
{
    /// <summary>Nothing: no press waited (it acted at once, its held use already fired, or it was cancelled).</summary>
    None,
    /// <summary>Let go before <see cref="InteractHold.HoldSeconds"/>: the plain use.</summary>
    Tap,
    /// <summary>Let go at or past <see cref="InteractHold.HoldSeconds"/> before a frame's <see cref="InteractHold.Tick"/> saw it: the held use.</summary>
    Hold,
}

/// <summary>
/// The interact key (E, or the VR grip) pressed and held: on a target that offers a held use (<c>IHoldInteractable</c>), a press
/// waits; let go before <see cref="HoldSeconds"/> it is a tap (the plain use), kept down that long the held use fires once (on the
/// frame's tick, or on the release if that comes first) and the release does nothing more. On anything else a press acts at once.
/// A second press restarts the wait. Times are in seconds. Engine-free.
/// </summary>
public sealed class InteractHold
{
    /// <summary>How long interact must be held down for the held use.</summary>
    public const double HoldSeconds = 1.0;

    private double _since;

    /// <summary>A press is down on a target that offers a held use, and neither its held use nor its tap has fired yet.</summary>
    public bool Holding { get; private set; }

    /// <summary>Interact went down at <paramref name="now"/>. True means act now (the plain use): nothing offers a held use.</summary>
    public bool Press(double now, bool holdOffered)
    {
        Holding = holdOffered;
        _since = now;
        return !holdOffered;
    }

    /// <summary>Interact came up: a tap on a held target (the plain use), the held use when it was down <see cref="HoldSeconds"/> or more, or nothing.</summary>
    public InteractRelease Release(double now)
    {
        if (!Holding) return InteractRelease.None;
        Holding = false;
        return now - _since < HoldSeconds ? InteractRelease.Tap : InteractRelease.Hold;
    }

    /// <summary>Every frame: true once, the frame the hold reaches <see cref="HoldSeconds"/>.</summary>
    public bool Tick(double now)
    {
        if (!Holding || now - _since < HoldSeconds) return false;
        Holding = false;
        return true;
    }

    /// <summary>Forget a press under way: it fires neither the held use nor the tap.</summary>
    public void Cancel() => Holding = false;
}
