namespace DungeonCrawler.Core;

/// <summary>Engine-free deterministic seeding helpers. Never use GD.Randi or Random.Shared for gameplay.</summary>
public static class Rng
{
    /// <summary>Stable 64-bit mix (SplitMix64 finalizer) so floor seeds never collide across game seeds.</summary>
    public static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    /// <summary>Seed for a given floor index derived from the game seed. Deterministic across runs and platforms.</summary>
    public static int FloorSeed(ulong gameSeed, int floorIndex)
        => unchecked((int)Mix(gameSeed ^ Mix((ulong)floorIndex)));

    public static Random ForFloor(ulong gameSeed, int floorIndex) => new(FloorSeed(gameSeed, floorIndex));

    /// <summary>FNV-1a over UTF-16 code units: the same number for the same string on every machine and run (string.GetHashCode is not).</summary>
    public static ulong StableHash(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var c in text) { h ^= c; h *= 1099511628211UL; }
        return h;
    }

    /// <summary>A Guid that every peer derives identically from the same key; for items the world places rather than rolls.</summary>
    public static Guid StableGuid(ulong key)
    {
        var a = Mix(key);
        var b = Mix(a ^ 0x5EEDBA5EUL);
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, a);
        BitConverter.TryWriteBytes(bytes[8..], b);
        return new Guid(bytes);
    }

    /// <summary>A per-actor seed on a floor: the floor seed salted with a stable id, so loot and rolls do not depend on node allocation order.</summary>
    public static int ActorSeed(ulong gameSeed, int floorIndex, int actorId) => unchecked((int)Mix((ulong)FloorSeed(gameSeed, floorIndex) ^ Mix((ulong)(uint)actorId + 0x1234UL)));
}
