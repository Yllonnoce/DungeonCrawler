namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// What a railed floor's props offer the hero looking at them, as one rule for every peer. Engine-free: the server's (or solo)
/// props call it with their own state, and a client's copies call it with the state the server told them (PropEvt and the prop
/// check's rail part), so a client offers exactly what the server will take. The server still judges every use when it arrives
/// (reach, the actor, and whatever the prompt cannot know: a key or a pick in the bag, a holder near the winch).
/// </summary>
public static class PropPromptRules
{
    /// <summary>A switch lever: nothing when it is the fixed return switch or its points are behind the cart this ride; else throw it, or set it back.</summary>
    public static string SwitchPrompt(bool isFixed, bool taken, bool thrown)
        => isFixed || taken ? "" : thrown ? "Set the switch back" : "Throw the switch";

    /// <summary>A stop's winch: offered while its gate is down (a holder still near refuses it on the server, with a log line).</summary>
    public static string WinchPrompt(bool gateUp) => gateUp ? "" : "Crank the winch";

    /// <summary>
    /// A cage wagon. Locked: the orc's keys open it, else Grisel's voice (<paramref name="griselOpens"/>: alive, up and near), else a
    /// key or a pick. Open: its crew are let out when the wagon cannot ride (<paramref name="canLetOut"/>), else a downed crew member
    /// aboard (<paramref name="downedAboard"/>, a name; null or empty: none) is revived; else nothing.
    /// </summary>
    public static string CagePrompt(bool open, bool orcKeys, bool griselOpens, bool canLetOut, string? downedAboard)
    {
        if (!open) return orcKeys ? "Open the cage (the orc's keys)" : griselOpens ? "Open the cage (Grisel)" : "Unlock the cage";
        if (canLetOut) return "Let them out";
        return string.IsNullOrEmpty(downedAboard) ? "" : Revive(downedAboard);
    }

    /// <summary>The last revive prompt built: a prompt is asked for every frame the wagon is looked at, so the same name's text is kept, not built again.</summary>
    private sealed record Built(string Name, string Text);
    private static Built? _revive;

    private static string Revive(string name)
    {
        var last = _revive; // one reference read: never a name from one call and a text from another
        if (last != null && last.Name == name) return last.Text;
        last = new Built(name, $"Revive {name}");
        _revive = last;
        return last.Text;
    }

    /// <summary>The orc's keyring at Grisel's belt: offered until the keys are taken.</summary>
    public static string KeyringPrompt(bool orcKeys) => orcKeys ? "" : "Take the orc's keys";
}
