using System.Globalization;
using System.Text;
using DungeonCrawler.Save;

namespace DungeonCrawler.Net;

/// <summary>
/// The state check's fingerprints (the bag and party half of the co-op repair loop; the item check is the floor half).
/// The server hashes what it holds, a client hashes its copy the same way, and a mismatch asks for a fresh sync. Only
/// lasting state goes in: health, stamina, mana and effect timers change every frame and travel in Vitals. The hash is
/// FNV-1a over an invariant string, so it is the same in every process (string.GetHashCode is randomised per process).
/// </summary>
public static class StateCheck
{
    public static uint Fnv(string s)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= 16777619u; }
        return h;
    }

    public static string BagSignature(IEnumerable<BagCellDto> bag)
        => string.Join("|", bag.OrderBy(c => c.Index).Select(c => $"{c.Index}:{c.Item.Id:N}:{c.Item.DefId}:{c.Item.StackCount}"));

    public static string MemberSignature(CharacterDto m)
    {
        var sb = new StringBuilder();
        sb.Append(m.Name).Append('|').Append(m.ClassId).Append('|').Append(m.Level.ToString(CultureInfo.InvariantCulture))
          .Append('|').Append(m.StatPoints.ToString(CultureInfo.InvariantCulture)).Append('|').Append(m.SkillPoints.ToString(CultureInfo.InvariantCulture))
          .Append('|').Append(m.Rank).Append('|').Append(m.Mangled ? 'M' : '-');
        sb.Append('|').Append(string.Join(",", m.Allocated.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}")));
        sb.Append('|').Append(string.Join(",", m.LearnedSkills.OrderBy(x => x, StringComparer.Ordinal)));
        sb.Append('|').Append(string.Join(",", m.Equipment.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value.Id:N}:{kv.Value.StackCount}")));
        return sb.ToString();
    }

    public static uint BagHash(IEnumerable<BagCellDto> bag) => Fnv(BagSignature(bag));
    public static uint MemberHash(CharacterDto m) => Fnv(MemberSignature(m));
}
