using DungeonCrawler.Adventure;
using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// A hero in a cart calls a moment of the ride (a cart bearing down on someone on foot, the beam, a leaping hound, a blocked
/// line, a gap to jump, the boarding) in their own voice: the first companion seated in <c>cart</c> and up, in party order, else a
/// seated player's hero (<see cref="RideLines.CallerIndex"/>), speaks a line of the trigger through the director's
/// <see cref="AdventureDirector.CallOut"/>. A call from the party's cart with nobody up in it to speak (every up hero rides a wagon of
/// the train) is spoken by the up hero riding the train nearest the call's place (the final review's M7). With nobody to speak, or no line
/// said (no campaign, not in a game), the log gets the plain words, so a warning is never lost. Server and single player only: a client
/// hears the line as a BarkMsg.
/// </summary>
public static class RailCalls
{
    /// <summary>A call was made: the trigger, and who said it (null when only the log had it). For the scripted checks.</summary>
    public static event Action<BarkTrigger, string?>? Called;

    /// <summary>
    /// Call <paramref name="trigger"/> from <paramref name="cart"/>; returns the speaker's name when a line was said, else null (and <paramref name="logIfSilent"/>
    /// is logged). <paramref name="place"/>: where the call is about (a leaping hound), for a speaker from the wagons; the cart when null.
    /// </summary>
    public static string? Say(PartyController party, ICartBody cart, BarkTrigger trigger, string logIfSilent, Node3D? place = null)
    {
        var heroes = party.Adventurers;
        var seats = new (bool InCart, bool Downed, bool Companion)[heroes.Length];
        for (var i = 0; i < heroes.Length; i++)
            if (GodotObject.IsInstanceValid(heroes[i])) seats[i] = (heroes[i].Cart == cart, heroes[i].Downed, heroes[i].Control == ControlKind.Companion);
        var at = RideLines.CallerIndex(seats);
        var speaker = at >= 0 ? heroes[at] : null;
        if (speaker == null && RailController.Instance is { } rails && GodotObject.IsInstanceValid(rails) && rails.Cart == cart)
            speaker = NearestInTrain(party, rails, place is { } p && GodotObject.IsInstanceValid(p) ? p : cart as Node3D);
        var said = speaker != null && AdventureDirector.Instance?.CallOut(speaker.State, trigger) == true ? speaker.State.Name : null;
        if (said == null) EventBus.Instance.Log(logIfSilent);
        Called?.Invoke(trigger, said);
        return said;
    }

    /// <summary>
    /// Call <paramref name="trigger"/> from the train (plan 14 Task 5, Ruling 16): the hero, companion or player, seated in a car of the train
    /// (<see cref="RailController.CarOf"/>), up, and nearest <paramref name="near"/> speaks it; with nobody to speak, or no line said,
    /// <paramref name="logIfSilent"/> is logged. Returns the speaker's name when a line was said, else null.
    /// </summary>
    public static string? SayNearest(PartyController party, Node3D near, BarkTrigger trigger, string logIfSilent)
    {
        var speaker = RailController.Instance is { } rails && GodotObject.IsInstanceValid(rails) ? NearestInTrain(party, rails, near) : null;
        var said = speaker != null && AdventureDirector.Instance?.CallOut(speaker.State, trigger) == true ? speaker.State.Name : null;
        if (said == null) EventBus.Instance.Log(logIfSilent);
        Called?.Invoke(trigger, said);
        return said;
    }

    /// <summary>The hero, companion or player, up and seated in a car of the train (<see cref="RailController.CarOf"/>) nearest <paramref name="near"/>, or null.</summary>
    private static Adventurer? NearestInTrain(PartyController party, RailController rails, Node3D? near)
    {
        if (near == null || !GodotObject.IsInstanceValid(near)) return null;
        Adventurer? speaker = null;
        var best = float.MaxValue;
        foreach (var hero in party.Adventurers)
        {
            if (!GodotObject.IsInstanceValid(hero) || hero.Downed || hero.Parked || rails.CarOf(hero) < 0) continue;
            var d = hero.GlobalPosition.DistanceSquaredTo(near.GlobalPosition);
            if (d < best) { best = d; speaker = hero; }
        }
        return speaker;
    }
}
