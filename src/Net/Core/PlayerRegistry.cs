using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

public enum ClaimResult { Ok, BadName, BadClass, NameInUse, Full, BadProtocol }

/// <summary>One player's character on this server, whether they are online or not.</summary>
public sealed class PlayerSlot
{
    public string Name { get; set; } = "";
    public string ClassId { get; set; } = "";
    public int MemberIndex { get; set; }
    public int PeerId { get; set; }
    public bool Online => PeerId > 0;
    public bool Ready { get; set; }
    public PoseDto? ParkedPose { get; set; }
    public int ParkedFloor { get; set; }
    public string LastSeenUtc { get; set; } = "";

    public NetPlayerDto ToDto() => new(Name, MemberIndex, ClassId, PeerId, Online);

    public PlayerDto ToSave() => new() { Name = Name, ClassId = ClassId, MemberIndex = MemberIndex, ParkedPose = ParkedPose, ParkedFloor = ParkedFloor, LastSeenUtc = LastSeenUtc };
    public static PlayerSlot FromSave(PlayerDto d) => new() { Name = d.Name, ClassId = d.ClassId, MemberIndex = d.MemberIndex, ParkedPose = d.ParkedPose, ParkedFloor = d.ParkedFloor, LastSeenUtc = d.LastSeenUtc };
}

/// <summary>
/// Who owns which party member. Names are the identity (case-insensitive, trimmed); a returning player gets their old
/// slot and stored class back; a new name gets a new member. At most <see cref="MaxOnline"/> peers at once.
/// </summary>
public sealed class PlayerRegistry
{
    private readonly List<PlayerSlot> _slots = new();

    public int MaxOnline { get; set; } = Protocol.MaxPlayers;
    public IReadOnlyList<PlayerSlot> All => _slots;
    public IEnumerable<PlayerSlot> Online => _slots.Where(s => s.Online);
    public int OnlineCount => _slots.Count(s => s.Online);

    public static string Normalize(string name) => name.Trim();
    public static bool ValidName(string name) => name.Length is >= 1 and <= Protocol.MaxNameLength && name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-');

    public PlayerSlot? ByName(string name) => _slots.FirstOrDefault(s => string.Equals(s.Name, Normalize(name), StringComparison.OrdinalIgnoreCase));
    public PlayerSlot? ByPeer(int peerId) => peerId <= 0 ? null : _slots.FirstOrDefault(s => s.PeerId == peerId);
    public PlayerSlot? ByMember(int memberIndex) => _slots.FirstOrDefault(s => s.MemberIndex == memberIndex);

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
            existing.PeerId = peerId;
            existing.Ready = false;
            slot = existing;
            return ClaimResult.Ok;
        }
        if (!classExists(classId)) return ClaimResult.BadClass;
        if (OnlineCount >= MaxOnline) return ClaimResult.Full;
        slot = new PlayerSlot { Name = name, ClassId = classId, PeerId = peerId, MemberIndex = createMember(name, classId) };
        _slots.Add(slot);
        return ClaimResult.Ok;
    }

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
        return slot;
    }

    /// <summary>Restore stored characters (a server save). Everyone starts offline.</summary>
    public void Load(IEnumerable<PlayerSlot> slots)
    {
        _slots.Clear();
        foreach (var s in slots) { s.PeerId = 0; s.Ready = false; _slots.Add(s); }
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
