using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Items;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>
/// The shrine: where it stands (one cell per floor from floor 2, deterministic from the layout), what it asks (one in three
/// is priced: blood, gold or a curse), what it offers (three boons weighted toward the houses the party holds, the crown
/// first once two of a house are held), and what walking away or lifting a curse pays or costs. Engine-free.
/// </summary>
public static class ShrineRules
{
    public const int FromFloor = 2, PricedOneIn = 3, OfferCount = 3, OfferCountCursed = 4, OfferCountFinePrint = 2, CrownNeeds = 2, HouseWeightPerHeld = 2;
    public const float BloodFraction = 0.25f;
    public const int SkipGoldBase = 40, SkipGoldPerFloor = 10, GoldPriceBase = 60, GoldPricePerFloor = 15, LiftBase = 80, LiftPerFloor = 20;
    public const int OfferActor = 9000, PriceActor = 9001;
    /// <summary>A room keeps at least this many free cells beside the altar: no shrine in a closet, and the planners keep their room.</summary>
    public const int MinFreeCellsBeside = 4;

    public enum Price { None, Blood, Gold, Curse }

    /// <summary>Test hook: every shrine asks this price when set.</summary>
    public static Price? ForcedPrice { get; set; }

    public static int GoldPrice(int floor) => GoldPriceBase + GoldPricePerFloor * floor;
    public static int SkipGold(int floor) => SkipGoldBase + SkipGoldPerFloor * floor;
    public static int LiftPrice(int floor) => LiftBase + LiftPerFloor * floor;
    public static bool CanPay(Price p, int gold, int floor) => p != Price.Gold || gold >= GoldPrice(floor);

    public static Price PriceFor(ulong gameSeed, int floor)
    {
        if (ForcedPrice is { } forced) return forced;
        var rng = new Random(Rng.ActorSeed(gameSeed, floor, PriceActor));
        return rng.Next(PricedOneIn) == 0 ? (Price)(1 + rng.Next(3)) : Price.None;
    }

    /// <summary>The line written on the altar before anyone touches it.</summary>
    public static string PriceLine(Price p, int floor) => p switch
    {
        Price.Blood => "It asks blood.",
        Price.Gold => $"It asks {GoldPrice(floor)} gold.",
        Price.Curse => "It asks a curse.",
        _ => "It asks nothing.",
    };

    /// <summary>
    /// The offers: boons not held, each weighted 1 + <see cref="HouseWeightPerHeld"/> per boon of its house the party holds; a house's crown
    /// enters only once <see cref="CrownNeeds"/> of it are held, and an eligible crown is always the first offer. Fewer than
    /// <paramref name="count"/> only when the pool runs out.
    /// </summary>
    public static List<string> Offer(ulong gameSeed, int floor, IReadOnlyList<string> held, int count)
    {
        var rng = new Random(Rng.ActorSeed(gameSeed, floor, OfferActor) ^ held.Count);
        var result = new List<string>();
        var pool = Boons.All.Where(b => !held.Contains(b.Id) && (!b.Crown || Boons.Held(held, b.House) >= CrownNeeds)).ToList();
        var crown = pool.FirstOrDefault(b => b.Crown);
        if (crown != null && count > 0) { result.Add(crown.Id); pool.Remove(crown); }
        pool.RemoveAll(b => b.Crown); // one crown an offer; the rest are drawn by house weight
        while (result.Count < count && pool.Count > 0)
        {
            var weights = pool.Select(b => (float)(1 + HouseWeightPerHeld * Boons.Held(held, b.House))).ToList();
            var i = LootRoller.WeightedIndex(weights, rng);
            result.Add(pool[i].Id);
            pool.RemoveAt(i);
        }
        return result;
    }

    /// <summary>
    /// The shrine's cell, or null when no room can hold one. Rooms other than the stairs', the arena, the safe room, the
    /// objective's jail and shrine, vaults and secret rooms; dead ends first, then nearest the midpoint of the stairs route.
    /// The cell: nearest the room's centre, plain floor at the room's level, nothing on it, a cell away from every entrance,
    /// and blocking it cuts nothing off.
    /// </summary>
    public static GridPos? Choose(FloorLayout layout)
    {
        if (layout.Rooms.Count == 0 || layout.Kind == LayoutKind.Tavern) return null;
        var upRoom = layout.RoomAt(layout.UpStairs);
        var downRoom = layout.RoomAt(layout.DownStairs);
        var safe = layout.SafeRoom >= 0 && layout.SafeRoom < layout.Rooms.Count ? layout.Rooms[layout.SafeRoom] : null;
        var rooms = layout.Rooms.Where(r => r != upRoom && r != downRoom && r != layout.BossArena && r != safe
            && r != layout.Objective?.JailRoom && r != layout.Objective?.ShrineRoom
            && layout.Vaults.All(v => v.Room != r) && layout.SecretRooms.All(s => s.Room != r)).ToList();
        if (rooms.Count == 0) return null;

        var route = GridPathfinder.FindPath(layout, layout.UpStairs, layout.DownStairs, maxExpansions: 20000);
        var mid = route is { Count: > 0 } ? route[route.Count / 2] : new GridPos((layout.UpStairs.X + layout.DownStairs.X) / 2, (layout.UpStairs.Y + layout.DownStairs.Y) / 2);
        var baseline = layout.Reachable(layout.UpStairs);
        var ordered = rooms.Select((r, i) => (room: r, index: i, deadEnd: FloorGenerator.Entrances(layout, r).Count == 1))
            .OrderByDescending(t => t.deadEnd).ThenBy(t => FloorGenerator.Dist2(t.room.Center, mid)).ThenBy(t => t.index).Select(t => t.room);
        foreach (var room in ordered)
        {
            var entrances = FloorGenerator.Entrances(layout, room);
            var level = layout.LevelOf(room.Center);
            var free = room.Cells().Where(c => Free(layout, c, level)).ToList();
            if (free.Count < MinFreeCellsBeside + 1) continue;
            foreach (var cell in free.OrderBy(c => c.ManhattanDistance(room.Center)).ThenBy(c => c.Y).ThenBy(c => c.X))
            {
                if (entrances.Any(e => e.ManhattanDistance(cell) <= 1)) continue;
                if (!baseline.Contains(cell)) continue;
                var with = layout.Reachable(layout.UpStairs, p => p == cell);
                if (with.Count != baseline.Count - 1) continue; // blocking the cell must cut off nothing else
                return cell;
            }
        }
        return null;
    }

    private static bool Free(FloorLayout layout, GridPos p, int level)
        => layout[p] == Cell.Floor && layout.LevelOf(p) == level && !layout.IsUnder(p) && !layout.IsChamfer(p, out _) && !layout.IsWater(p) && !layout.Ramps.ContainsKey(p)
           && !layout.IsReserved(p) && p != layout.UpStairs && p != layout.DownStairs
           && !layout.ChestSpawns.Contains(p) && !layout.EnemySpawns.Contains(p) && !layout.BreakableSpawns.Contains(p) && !layout.TrapSpawns.Contains(p) && !layout.KeySpawns.Contains(p)
           && !layout.LedgeSpawns.Contains(p) && !layout.CellarSpawns.Contains(p)
           && layout.Vaults.All(v => v.Key != p && v.Lever != p && v.Door != p)
           && layout.Ledges.All(l => !l.Cells.Contains(p) && l.RampBottom != p && l.RampTop != p && l.Chest != p)
           && layout.Cellars.All(c => c.Pit != p && c.ExitTarget != p && !c.Exit.Contains(p))
           && layout.Secrets.All(s => s.Facing != p && s.Alcove != p);
}
