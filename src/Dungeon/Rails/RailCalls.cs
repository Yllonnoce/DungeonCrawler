using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A hero in a cart calls a moment of the ride (a cart bearing down on someone on foot, the beam, a leaping hound, a blocked
/// line, a gap to jump, the boarding) in their own voice: the first seated hero in <c>cart</c> who is not down, in party order
/// (a player's hero too: the line is still that hero's), speaks a line of the trigger through the director's
/// <see cref="AdventureDirector.CallOut"/>. With nobody to speak, or no line said (no campaign, not in a game), the log gets
/// the plain words, so a warning is never lost. Server and single player only: a client hears the line as a BarkMsg.
/// </summary>
public static class RailCalls
{
    /// <summary>A call was made: the trigger, and who said it (null when only the log had it). For the scripted checks.</summary>
    public static event Action<BarkTrigger, string?>? Called;

    /// <summary>Call <paramref name="trigger"/> from <paramref name="cart"/>; returns the speaker's name when a line was said, else null (and <paramref name="logIfSilent"/> is logged).</summary>
    public static string? Say(PartyController party, ICartBody cart, BarkTrigger trigger, string logIfSilent)
    {
        Adventurer? speaker = null;
        foreach (var hero in party.Adventurers)
            if (GodotObject.IsInstanceValid(hero) && hero.Cart == cart && !hero.Downed) { speaker = hero; break; }
        var said = speaker != null && AdventureDirector.Instance?.CallOut(speaker.State, trigger) == true ? speaker.State.Name : null;
        if (said == null) EventBus.Instance.Log(logIfSilent);
        Called?.Invoke(trigger, said);
        return said;
    }
}
