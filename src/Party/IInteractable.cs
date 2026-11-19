namespace DungeonCrawler.Party;

/// <summary>World objects the party can use by looking at them and pressing interact.</summary>
public interface IInteractable
{
    /// <summary>Short verb phrase shown as the HUD prompt, e.g. "Pick up Rusty Sword".</summary>
    string Prompt { get; }
    /// <summary>Use it. <paramref name="actor"/> is the body that reached for it (loot spills toward them, a slip springs on them).</summary>
    void Interact(PartyController party, Adventurer actor);
}

/// <summary>
/// A world object that also offers a held use: interact (E, or the VR grip) held down for <see cref="InteractHold.HoldSeconds"/>
/// while looking at it calls <see cref="HoldInteract"/> instead of <see cref="IInteractable.Interact"/>, and a tap released sooner
/// still calls <see cref="IInteractable.Interact"/> (plan 7: the party's cart, held at a stop by its locked cage, lets a seated
/// player leave the prisoners behind).
/// </summary>
public interface IHoldInteractable : IInteractable
{
    /// <summary>The held use is offered to the local hero now (a press then waits to see if it is held).</summary>
    bool HoldOffered { get; }
    /// <summary>The held use. <paramref name="actor"/> is the body that held interact.</summary>
    void HoldInteract(PartyController party, Adventurer actor);
}
