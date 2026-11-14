using DungeonCrawler.Core;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Net;

/// <summary>
/// The bag guard (v14): who a player may act for, and whether the cell they act on still holds what their screen showed.
/// The party bag is shared and changed by many hands at once (bots drink and pick up, arrows run out, other players move
/// things), so an intent that names a cell by index alone can land on a different item. A refused intent is answered at
/// once with that player's own copy of the bag (and the member involved) and a short line, so their screen settles and
/// they can try again; nothing is destroyed, sold or dropped by mistake, and nothing is refused in silence.
/// </summary>
public partial class ServerWorld
{
    public const string ItemMovedLine = "That item moved; try again.";
    public const string NotYoursLine = "You can only change your own hero and your own bots.";

    /// <summary>Whether <paramref name="slot"/>'s player may act for <paramref name="member"/> (their hero or a bot that follows them); tells them and re-sends that member when not.</summary>
    private bool MayActFor(PlayerSlot slot, int member)
    {
        if (Registry.MayChange(slot, member)) return true;
        RefuseBag(slot, NotYoursLine, member, "bag-not-yours");
        return false;
    }

    /// <summary>The shared bag is anyone's; a doll's slot only its owner's (a player's hero, or a bot they own).</summary>
    private bool MayTouch(PlayerSlot slot, BagSlot a, BagSlot b)
    {
        if (a.IsEquip && !Registry.MayChange(slot, a.Member)) { RefuseBag(slot, NotYoursLine, a.Member, "bag-not-yours"); return false; }
        if (b.IsEquip && !Registry.MayChange(slot, b.Member)) { RefuseBag(slot, NotYoursLine, b.Member, "bag-not-yours"); return false; }
        return true;
    }

    /// <summary>Whether <paramref name="at"/> still holds the item the player's screen showed (<paramref name="expect"/>).</summary>
    private bool StillHolds(PlayerSlot slot, BagSlot at, Guid expect)
    {
        var party = Main.Instance.Party;
        if (BagActions.Holds(party.Bag, party.Members, at, expect)) return true;
        RefuseBag(slot, ItemMovedLine, at.IsEquip ? at.Member : -1, "bag-moved");
        return false;
    }

    private readonly Dictionary<(int Peer, string Why), double> _lastBagRefusal = new();

    private void RefuseBag(PlayerSlot slot, string line, int member, string trace)
    {
        var party = Main.Instance.Party;
        // a burst of stale intents (a fast double-click, or a client gone wrong) costs one reply a quarter second, never one each
        var now = Time.GetTicksMsec() / 1000.0;
        if (_lastBagRefusal.TryGetValue((slot.PeerId, trace), out var last) && now - last < 0.25) { Trace.Add(trace); return; }
        _lastBagRefusal[(slot.PeerId, trace)] = now;
        Tell(slot, line);
        SendTo(slot.PeerId, new BagSync(Save.SaveMapper.ToDto(party.Bag)));
        if (member >= 0 && member < party.Members.Length) SendTo(slot.PeerId, new PartySync(member, Save.SaveMapper.ToDto(party.Members[member])));
        Trace.Add(trace);
        if (NetSession.Instance.TestServer) GD.Print($"[net-server] {slot.Name}'s bag intent refused ({trace}{(member >= 0 ? $", member {member}" : "")}): sent their copy again");
    }
}
