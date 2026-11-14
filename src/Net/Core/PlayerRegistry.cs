using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

public enum ClaimResult { Ok, BadName, BadClass, NameInUse, Full, BadProtocol }

/// <summary>What became of a request to bring a hero bot in or send one away.</summary>
public enum BotResult { Ok, NotPlaying, NameTaken, HeroInPlay, Full, NotABot, NotYours }

/// <summary>
/// One player's character on this server, whether they are online or not; or a hero bot (<see cref="BotOwner"/> set): a
/// story hero a player brought in to fill an empty place, run by the server's companion AI and following that player.
/// </summary>
public sealed class PlayerSlot
{
    public string Name { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int MemberIndex { get; set; }
    public int PeerId { get; set; }
    public bool Online => PeerId > 0;
    public bool Ready { get; set; }
    /// <summary>The floor the peer said it built when it sent Ready (a join can race a floor change).</summary>
    public int ReadyFloor { get; set; }
    public PoseDto? ParkedPose { get; set; }
    public int ParkedFloor { get; set; }
    public string LastSeenUtc { get; set; } = "";

    /// <summary>The player a hero bot follows and answers to; null for a player's own character.</summary>
    public string? BotOwner { get; set; }
    /// <summary>A bot in the world right now.</summary>
    public bool BotActive { get; set; }
    /// <summary>A bot set aside because everyone left: it comes back when its owner does.</summary>
    public bool BotWaiting { get; set; }
    /// <summary>When the bot was last brought in (the newest steps aside first).</summary>
    public long BotOrder { get; set; }
    public bool IsBot => BotOwner != null;
    /// <summary>A body in the world: an online player or an active bot.</summary>
    public bool InPlay => Online || (IsBot && BotActive);

    public NetPlayerDto ToDto() => new(Name, MemberIndex, ClassId, PeerId, InPlay, BotOwner);

    public PlayerDto ToSave() => new() { Name = Name, ClassId = ClassId, MemberIndex = MemberIndex, ParkedPose = ParkedPose, ParkedFloor = ParkedFloor, LastSeenUtc = LastSeenUtc, BotOwner = BotOwner, BotActive = BotActive, BotWaiting = BotWaiting };
    public static PlayerSlot FromSave(PlayerDto d) => new() { Name = d.Name, ClassId = d.ClassId, MemberIndex = d.MemberIndex, ParkedPose = d.ParkedPose, ParkedFloor = d.ParkedFloor, LastSeenUtc = d.LastSeenUtc, BotOwner = d.BotOwner, BotActive = d.BotActive, BotWaiting = d.BotWaiting };
}

/// <summary>
/// Who owns which party member. Names are the identity (case-insensitive, trimmed); a returning player gets their old
/// slot and stored class back; a new name gets a new member. At most <see cref="MaxOnline"/> peers at once.
/// Hero bots fill the places players leave empty: at most <see cref="MaxOnline"/> heroes are in play, players and bots
/// together, and players always come first (a player joining a full party sends the newest bot aside).
/// </summary>
public sealed class PlayerRegistry
{
    private readonly List<PlayerSlot> _slots = new();
    private long _botOrder;

    public int MaxOnline { get; set; } = Protocol.MaxPlayers;
    public IReadOnlyList<PlayerSlot> All => _slots;
    public IEnumerable<PlayerSlot> Online => _slots.Where(s => s.Online);
    public int OnlineCount => _slots.Count(s => s.Online);
    /// <summary>Heroes in the world: online players and active bots.</summary>
    public int InPlayCount => _slots.Count(s => s.InPlay);
    public IEnumerable<PlayerSlot> ActiveBots => _slots.Where(s => s.IsBot && s.BotActive);

    /// <summary>A bot was sent aside to make room for a joining player (the host parks its body).</summary>
    public Action<PlayerSlot>? BotSteppedAside { get; set; }
    /// <summary>A bot's owner left: the bot now follows another player, or (null) waits out of the world for its owner.</summary>
    public Action<PlayerSlot, PlayerSlot?>? BotHandedOver { get; set; }

    public static string Normalize(string name) => name.Trim();
    public static bool ValidName(string name) => name.Length is >= 1 and <= Protocol.MaxNameLength && name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-');

    public PlayerSlot? ByName(string name) => _slots.FirstOrDefault(s => string.Equals(s.Name, Normalize(name), StringComparison.OrdinalIgnoreCase));
    public PlayerSlot? ByPeer(int peerId) => peerId <= 0 ? null : _slots.FirstOrDefault(s => s.PeerId == peerId);
    public PlayerSlot? ByMember(int memberIndex) => _slots.FirstOrDefault(s => s.MemberIndex == memberIndex);

    /// <summary>
    /// Whether <paramref name="player"/> may change <paramref name="memberIndex"/>'s gear, points and skills, or use an item on
    /// them: their own hero, or a bot in play that follows them. Everyone else's hero is theirs to manage.
    /// </summary>
    public bool MayChange(PlayerSlot player, int memberIndex)
        => memberIndex >= 0 && (memberIndex == player.MemberIndex
            || ByMember(memberIndex) is { IsBot: true, BotActive: true } bot && string.Equals(bot.BotOwner, player.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A peer asks for a name and class. <paramref name="createMember"/> makes a new party member and returns its index;
    /// it is only called for a name this server has never seen. The stored class wins for a returning name.
    /// </summary>
    public ClaimResult Claim(int peerId, string name, string classId, Func<string, bool> classExists, Func<string, string, int> createMember, out PlayerSlot? slot)
    {
        slot = null;
        name = Normalize(name);
        if (!ValidName(name)) return ClaimResult.BadName;
        var existing = ByName(name);
        if (existing != null)
        {
            if (existing.Online) return ClaimResult.NameInUse;
            if (OnlineCount >= MaxOnline) return ClaimResult.Full;
            // joining under a bot's name takes that hero over; an active bot's place is the joiner's, so no room is needed
            var tookActiveBot = existing.IsBot && existing.BotActive;
            if (existing.IsBot) { existing.BotOwner = null; existing.BotActive = false; existing.BotWaiting = false; }
            if (!tookActiveBot) MakeRoom();
            existing.PeerId = peerId;
            existing.Ready = false;
            slot = existing;
            return ClaimResult.Ok;
        }
        if (!classExists(classId)) return ClaimResult.BadClass;
        if (OnlineCount >= MaxOnline) return ClaimResult.Full;
        MakeRoom();
        slot = new PlayerSlot { Name = name, ClassId = classId, PeerId = peerId, MemberIndex = createMember(name, classId) };
        _slots.Add(slot);
        return ClaimResult.Ok;
    }

    /// <summary>A player is about to come in: if the party is full, the newest bot steps aside.</summary>
    private void MakeRoom()
    {
        if (InPlayCount < MaxOnline) return;
        var newest = ActiveBots.OrderByDescending(b => b.BotOrder).FirstOrDefault();
        if (newest == null) return;
        newest.BotActive = false;
        BotSteppedAside?.Invoke(newest);
    }

    /// <summary>
    /// The online player <paramref name="owner"/> brings a hero in as a bot. A hero set aside before comes back as it
    /// was (its member keeps its level and gear); a new one gets a member from <paramref name="createMember"/>.
    /// </summary>
    public BotResult AddBot(string owner, string hero, string classId, Func<string, string, int> createMember, out PlayerSlot? slot)
    {
        slot = null;
        var who = ByName(owner);
        if (who is not { Online: true }) return BotResult.NotPlaying;
        var existing = ByName(hero);
        if (existing is { IsBot: false }) return BotResult.NameTaken;
        if (existing is { BotActive: true }) return BotResult.HeroInPlay;
        if (InPlayCount >= MaxOnline) return BotResult.Full;
        slot = existing ?? new PlayerSlot { Name = Normalize(hero), ClassId = classId, MemberIndex = createMember(Normalize(hero), classId) };
        if (existing == null) _slots.Add(slot);
        slot.BotOwner = who.Name;
        slot.BotActive = true;
        slot.BotWaiting = false;
        slot.BotOrder = ++_botOrder;
        return BotResult.Ok;
    }

    /// <summary>The owner sends their bot away; it keeps its member for next time.</summary>
    public BotResult DismissBot(string owner, int memberIndex, out PlayerSlot? slot)
    {
        slot = ByMember(memberIndex);
        if (slot is not { IsBot: true, BotActive: true }) { slot = null; return BotResult.NotABot; }
        if (!string.Equals(slot.BotOwner, Normalize(owner), StringComparison.OrdinalIgnoreCase)) { slot = null; return BotResult.NotYours; }
        slot.BotActive = false;
        slot.BotWaiting = false;
        return BotResult.Ok;
    }

    /// <summary>The player is back: the bots that waited for them come in again, while there is room. Returns them.</summary>
    public List<PlayerSlot> ResumeBots(PlayerSlot player)
    {
        var back = new List<PlayerSlot>();
        foreach (var bot in _slots.Where(s => s.IsBot && s.BotWaiting && string.Equals(s.BotOwner, player.Name, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.BotOrder).ToList())
        {
            if (InPlayCount >= MaxOnline) break;
            bot.BotActive = true;
            bot.BotWaiting = false;
            back.Add(bot);
        }
        return back;
    }

    /// <summary>The bots that follow <paramref name="player"/>.</summary>
    public IEnumerable<PlayerSlot> BotsOf(PlayerSlot player) => ActiveBots.Where(b => string.Equals(b.BotOwner, player.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Which of <paramref name="heroes"/> a player could bring in now: not in play and not a player's own name.</summary>
    public List<(string Name, string ClassId)> HeroesOnOffer(IEnumerable<(string Name, string ClassId)> heroes) => HeroBots.Offer(heroes, ToDtos());

    /// <summary>The peer went away: remember where its body stood.</summary>
    public PlayerSlot? Leave(int peerId, PoseDto? pose, int floor, string nowUtc)
    {
        var slot = ByPeer(peerId);
        if (slot == null) return null;
        slot.PeerId = 0;
        slot.Ready = false;
        slot.ParkedPose = pose;
        slot.ParkedFloor = floor;
        slot.LastSeenUtc = nowUtc;
        // their bots follow whoever has the fewest, or wait out of the world for them if nobody is left
        foreach (var bot in BotsOf(slot).ToList())
        {
            var to = Online.OrderBy(p => BotsOf(p).Count()).ThenBy(p => p.MemberIndex).FirstOrDefault();
            if (to != null) bot.BotOwner = to.Name;
            else { bot.BotActive = false; bot.BotWaiting = true; }
            BotHandedOver?.Invoke(bot, to);
        }
        return slot;
    }

    /// <summary>Restore stored characters (a server save). Everyone starts offline.</summary>
    public void Load(IEnumerable<PlayerSlot> slots)
    {
        _slots.Clear();
        foreach (var s in slots)
        {
            s.PeerId = 0; s.Ready = false;
            if (s.IsBot && s.BotActive) { s.BotActive = false; s.BotWaiting = true; } // a bot in play when the server stopped comes back with its owner
            s.BotOrder = ++_botOrder;
            _slots.Add(s);
        }
    }

    public List<NetPlayerDto> ToDtos() => _slots.Select(s => s.ToDto()).ToList();
    public List<PlayerDto> ToSave() => _slots.Select(s => s.ToSave()).ToList();

    public static string ReasonText(ClaimResult r) => r switch
    {
        ClaimResult.BadName => "Pick a name of 1 to 16 letters, digits, spaces, _ or -.",
        ClaimResult.BadClass => "That class does not exist on this server.",
        ClaimResult.NameInUse => "Someone with that name is already playing.",
        ClaimResult.Full => "The server is full.",
        ClaimResult.BadProtocol => "Your game version does not match the server.",
        _ => "",
    };
}

/// <summary>Which story heroes a player can bring into a co-op party (shared by the server's check and the pause menu).</summary>
public static class HeroBots
{
    /// <summary>The heroes not in play and not a player's own name, in the order given.</summary>
    public static List<(string Name, string ClassId)> Offer(IEnumerable<(string Name, string ClassId)> heroes, IEnumerable<NetPlayerDto> roster)
    {
        var known = roster.ToList();
        return heroes.Where(h => !known.Any(p => string.Equals(p.Name, h.Name, StringComparison.OrdinalIgnoreCase) && (p.Online || p.BotOwner == null))).ToList();
    }

    /// <summary>The line everyone reads when a bot cannot come or go.</summary>
    public static string ReasonText(BotResult r, string hero) => r switch
    {
        BotResult.NotPlaying => "Only a player in the game can call a hero in.",
        BotResult.NameTaken => $"{hero} is a player's name on this server.",
        BotResult.HeroInPlay => $"{hero} is already with the party.",
        BotResult.Full => "The party already has four heroes.",
        BotResult.NotABot => "That is not a hero bot.",
        BotResult.NotYours => "Only the player who called that hero in can send them away.",
        _ => "",
    };
}
