namespace DungeonCrawler.Party;

/// <summary>World objects the party can use by looking at them and pressing interact.</summary>
public interface IInteractable
{
    /// <summary>Short verb phrase shown as the HUD prompt, e.g. "Pick up Rusty Sword".</summary>
    string Prompt { get; }
    /// <summary>Use it. <paramref name="actor"/> is the body that reached for it (loot spills toward them, a slip springs on them).</summary>
    void Interact(PartyController party, Adventurer actor);
}
