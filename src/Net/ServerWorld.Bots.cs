using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// Hero bots: a player fills an empty place with one of the story heroes (Brand, Sable, Wren, Ilex). The server runs
/// the bot with the single-player companion AI, following the player who called it in; clients see it like any other
/// body. The rules (four heroes at most, players first, hand-overs) live in <see cref="PlayerRegistry"/>.
/// </summary>
public partial class ServerWorld
{
    private void StartBots()
    {
        Registry.BotSteppedAside = bot =>
        {
            SetAside(bot);
            EventBus.Instance.Log($"{bot.Name} steps back to make room.");
        };
        Registry.BotHandedOver = (bot, to) =>
        {
            if (to == null)
            {
                SetAside(bot);
                EventBus.Instance.Log($"{bot.Name} waits for {bot.BotOwner} to come back.");
                return;
            }
            if (BodyOf(bot) is { } body) body.Leader = BodyOf(to);
            Broadcast(new PlayerJoined(bot.ToDto()));
            EventBus.Instance.Log($"{bot.Name} now follows {to.Name}.");
            Trace.Add($"bot-follows:{bot.Name}:{to.Name}");
        };
        // every member's swing, shot or cast shows on every screen (the swinger's own screen already played it)
        EventBus.Instance.AbilityUsed += (index, kind) =>
        {
            if (Quiet || Main.Instance?.Party is not { } party || index < 0 || index >= party.Members.Length) return;
            Broadcast(new SwingEvt(index, (byte)kind, party.Members[index].WeaponProfile.IsRanged));
        };
    }

    private static Adventurer? BodyOf(PlayerSlot slot)
    {
        var party = Main.Instance?.Party;
        return party != null && slot.MemberIndex >= 0 && slot.MemberIndex < party.Adventurers.Length ? party.Adventurers[slot.MemberIndex] : null;
    }

    /// <summary>Tell one player why their request came to nothing.</summary>
    private void Tell(PlayerSlot slot, string line)
    {
        if (slot.PeerId > 0 && line != "") SendTo(slot.PeerId, new LogMsg(new List<string> { line }));
    }

    private void OnAddBot(PlayerSlot owner, string heroName)
    {
        var hero = PartyController.DefaultRoster.FirstOrDefault(h => string.Equals(h.Name, heroName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (hero.Name == null) { Tell(owner, "There is no hero by that name."); return; }
        var result = Registry.AddBot(owner.Name, hero.Name, hero.ClassId, (name, cls) => Main.Instance.Party.AddMember(name, cls, hero.Rank), out var bot);
        if (result != BotResult.Ok || bot == null) { Tell(owner, HeroBots.ReasonText(result, hero.Name)); return; }
        BringIn(bot, owner, $"{owner.Name} calls {bot.Name} into the party.");
    }

    private void OnDismissBot(PlayerSlot owner, int member)
    {
        var result = Registry.DismissBot(owner.Name, member, out var bot);
        if (result != BotResult.Ok || bot == null) { Tell(owner, HeroBots.ReasonText(result, "")); return; }
        SetAside(bot);
        EventBus.Instance.Log($"{owner.Name} sends {bot.Name} back to town.");
    }

    /// <summary>A player came in: the bots that waited for them come back with them.</summary>
    private void ResumeBotsOf(PlayerSlot player)
    {
        if (BodyOf(player) is { } body) body.Leader = null; // a player who took a bot's name over plays that body now
        foreach (var bot in Registry.ResumeBots(player)) BringIn(bot, player, $"{bot.Name} comes back with {player.Name}.");
    }

    /// <summary>Put the bot's body in the world just behind its owner, run by the companion AI, and tell everyone.</summary>
    private void BringIn(PlayerSlot bot, PlayerSlot owner, string line)
    {
        var party = Main.Instance.Party;
        var index = bot.MemberIndex;
        if (BodyOf(owner) is not { } lead || index < 0 || index >= party.Adventurers.Length) return;
        var at = lead.GlobalPosition + lead.GlobalTransform.Basis * new Vector3(0, 0, 1.6f);
        if (Main.Instance.Builder.Layout is { } layout && !layout.IsWalkable(DungeonBuilder.WorldToCell(at))) at = lead.GlobalPosition;
        party.Unpark(index, ControlKind.Companion, at + Vector3.Up * 0.3f, lead.GlobalPosition - lead.GlobalTransform.Basis.Z * 4f);
        party.Adventurers[index].Leader = lead;
        _dirtyMembers.Add(index);
        Broadcast(new PlayerJoined(bot.ToDto()));
        EventBus.Instance.Log(line);
        Trace.Add($"bot:{bot.Name}");
        GD.Print($"[net-server] bot {bot.Name} (member {index}, {bot.ClassId}) follows {owner.Name}");
        WriteStatus();
    }

    /// <summary>Take the bot's body out of the world (it keeps its level and gear) and tell everyone.</summary>
    private void SetAside(PlayerSlot bot)
    {
        var party = Main.Instance?.Party;
        if (party != null && GodotObject.IsInstanceValid(party) && BodyOf(bot) is { } body)
        {
            body.Leader = null;
            party.Park(bot.MemberIndex);
        }
        Broadcast(new PlayerLeft(bot.MemberIndex, bot.Name));
        Trace.Add($"bot-aside:{bot.Name}");
        WriteStatus();
    }
}
