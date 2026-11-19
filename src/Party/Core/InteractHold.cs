namespace DungeonCrawler.Party;

/// <summary>
/// The interact key (E, or the VR grip) pressed and held: on a target that offers a held use (<c>IHoldInteractable</c>), a press
/// waits; let go before <see cref="HoldSeconds"/> it is a tap (the plain use), kept down that long the held use fires once and the
/// release does nothing. On anything else a press acts at once. Times are in seconds. Engine-free.
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

    /// <summary>Interact came up. True means it was a tap on a held target, so act now (the plain use).</summary>
    public bool Release(double now)
    {
        var tap = Holding && now - _since < HoldSeconds;
        Holding = false;
        return tap;
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
