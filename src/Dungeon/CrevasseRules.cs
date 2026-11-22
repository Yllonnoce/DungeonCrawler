using System.Numerics;
using DungeonCrawler.Dungeon.Generation;

namespace DungeonCrawler.Dungeon;

/// <summary>What fell into a crevasse, which decides its fate (<see cref="CrevasseRules.FateOf"/>).</summary>
public enum FallBody { Hero, Crew, Escort, OtherNpc, Enemy }

/// <summary>
/// A fall's end: a hero is dead until the party next reaches a stop, crew and other NPCs are lost, an escort is caught at the lip
/// (down, revivable, still escorting), an enemy dies.
/// </summary>
public enum FallFate { Dead, Lost, CaughtAtLip, Dies }

/// <summary>
/// The crevasses' rules: who fell (a body over a chasm or bridge cell, <see cref="FallDepth"/> below that cell's floor), what
/// becomes of them, where an escort is laid at the lip, and where code may set a body down. Also the footing a body the game
/// steers (a companion, a hero bot) keeps: never over a chasm cell, on a plank's cell only on its boards
/// (<see cref="Footing"/>, <see cref="KeepFooting"/>, <see cref="FollowSpot"/>). Lethality never changes any of it. Engine-free;
/// world points are (X, Z) in a <see cref="Vector2"/>.
/// </summary>
public static class CrevasseRules
{
    /// <summary>A body this far below the floor of the drop cell it is over has fallen.</summary>
    public const float FallDepth = 2.5f;
    /// <summary>How far below a chasm's lip a diagonal (corner) neighbour's ground may sit: the observed metre the generator's corners reach.</summary>
    public const float CornerLipSlack = 1f;
    /// <summary>A fallen hero's body is held this far below the lip: above <c>Adventurer.FellOutY</c>, so it never trips the fell-out guard.</summary>
    public const float HoldDepth = 6f;
    /// <summary>A fallen hero comes back with this fraction of max HP.</summary>
    public const float ReturnHpFraction = 0.25f;
    /// <summary>A hero on foot this close (cells, Chebyshev) to a chasm region is warned of it, once a region per floor visit.</summary>
    public const int WarnCells = 2;
    /// <summary>An escort is laid on a cell off the lanes within this many cells of the fall (Ruling 12)...</summary>
    public const int LipReach = 3;
    /// <summary>...else on the nearest walkable cell within this many.</summary>
    public const int LipSearch = 6;
    /// <summary>A party-lane cell with a chasm this close (Chebyshev) is over a crevasse (Ruling 7).</summary>
    public const int OverReach = 2;

    /// <summary>A plank's boards across its hall: <c>ChasmDressing</c> lays its collision box this wide.</summary>
    public const float PlankWidth = 1.6f;
    /// <summary>A steered body's centre keeps this far inside a plank's edge.</summary>
    public const float FootingMargin = 0.2f;
    /// <summary>A steered body looks this far ahead of its centre (its radius and a little)...</summary>
    public const float ProbeAhead = 0.35f;
    /// <summary>...plus the ground it covers in this long at the speed it would walk.</summary>
    public const float ProbeSeconds = 0.1f;
    /// <summary>Followers in file along a bridge stand this far apart, the first this far behind the leader.</summary>
    public const float FileGap = 1.5f;

    /// <summary>A drop cell: a chasm, or a bridge over one (a plank or deck is walkable, but a body off its edge falls).</summary>
    public static bool IsDrop(FloorLayout l, GridPos c) => l.IsChasm(c) || l.IsBridge(c);

    /// <summary>Has a body at height <paramref name="y"/> over <paramref name="c"/> fallen? Only over a drop, below the cell's own floor <paramref name="floorY"/> (its lip).</summary>
    public static bool Fell(FloorLayout l, GridPos c, float y, float floorY) => IsDrop(l, c) && y < floorY - FallDepth;

    public static FallFate FateOf(FallBody b) => b switch
    {
        FallBody.Hero => FallFate.Dead,
        FallBody.Crew => FallFate.Lost,
        FallBody.Escort => FallFate.CaughtAtLip,
        FallBody.OtherNpc => FallFate.Lost,
        _ => FallFate.Dies,
    };

    /// <summary>
    /// What a fallen NPC counts as: crew are crew; an NPC escorting the party, or one named as the line's escort
    /// (<paramref name="namedEscort"/>: Grisel, of <c>CrewRescueController.EscortIds</c>) whether or not she is escorting at that
    /// moment, is an escort (caught at the lip: Grisel never dies in a fall); any other NPC is lost.
    /// </summary>
    public static FallBody NpcBody(bool crew, bool escorting, bool namedEscort)
        => crew ? FallBody.Crew : escorting || namedEscort ? FallBody.Escort : FallBody.OtherNpc;

    /// <summary>
    /// Where an escort who fell near <paramref name="near"/> is laid (Ruling 12): the nearest cell a body may be set down on
    /// (<see cref="SetDownOk"/>) that is no lane cell (a pocket counts) within <see cref="LipReach"/> cells, else the nearest
    /// within <see cref="LipSearch"/>; nearest by straight distance, ties by (Y, X). Null when there is none.
    /// </summary>
    public static GridPos? LipCell(FloorLayout l, GridPos near)
        => Nearest(l, near, LipReach, c => SetDownOk(l, c) && !l.IsRail(c)) ?? Nearest(l, near, LipSearch, c => SetDownOk(l, c));

    /// <summary>The nearest cell anywhere on the floor a body may be set down on (<see cref="SetDownOk"/>), ties by (Y, X); null when there is none.</summary>
    public static GridPos? NearestSetDown(FloorLayout l, GridPos near)
        => Nearest(l, near, Math.Max(l.Width, l.Height), c => SetDownOk(l, c));

    /// <summary>
    /// Where an escort who fell near <paramref name="near"/> is laid: the <see cref="LipCell"/>, else the <see cref="NearestSetDown"/>
    /// anywhere on the floor (any level), else the nearest walkable cell at all (a bridge: its middle, on the boards, holds a body);
    /// never a chasm cell. Null when the floor has no walkable cell (unreachable: the caller lays her on the line's stop platform).
    /// A fall never kills an escort.
    /// </summary>
    public static GridPos? EscortCell(FloorLayout l, GridPos near)
        => LipCell(l, near) ?? NearestSetDown(l, near) ?? Nearest(l, near, Math.Max(l.Width, l.Height), l.IsWalkable); // a chasm cell is never walkable

    private static GridPos? Nearest(FloorLayout l, GridPos near, int reach, Func<GridPos, bool> ok)
    {
        GridPos? best = null;
        var bestD = int.MaxValue;
        for (var dy = -reach; dy <= reach; dy++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                var c = new GridPos(near.X + dx, near.Y + dy);
                var d = dx * dx + dy * dy;
                if (!ok(c)) continue;
                if (d < bestD || (d == bestD && best is { } b && (c.Y < b.Y || (c.Y == b.Y && c.X < b.X)))) { best = c; bestD = d; }
            }
        return best;
    }

    /// <summary>A party-lane cell over a crevasse (Ruling 7): a bridge, or a chasm within <see cref="OverReach"/> cells (Chebyshev).</summary>
    public static bool OverCrevasse(FloorLayout l, GridPos laneCell)
    {
        if (l.IsBridge(laneCell)) return true;
        for (var dy = -OverReach; dy <= OverReach; dy++)
            for (var dx = -OverReach; dx <= OverReach; dx++)
                if (l.IsChasm(new GridPos(laneCell.X + dx, laneCell.Y + dy))) return true;
        return false;
    }

    /// <summary>
    /// A floor built anew brings every fallen hero back (ruling 8), parked ones too: on a floor change (the stairs), and on a load or
    /// a rebuild of a floor with no line. A floor with a line brings them back beside its cart (<c>RailController.Restore</c>).
    /// </summary>
    public static bool ReturnOnRebuild(bool floorChange, bool lineOnFloor) => floorChange || !lineOnFloor;

    /// <summary>
    /// A player rejoins at the pose their body was parked at (cell <paramref name="cell"/>, height <paramref name="y"/> over its floor
    /// <paramref name="floorY"/>) only when they were not fallen and the pose is not below a drop's lip (<see cref="Fell"/>): a fallen
    /// body's pose is its hold in the dark, and one parked mid-fall would be taken again. Else they come in at the stairs.
    /// </summary>
    public static bool RejoinAtParkedPose(FloorLayout l, GridPos cell, float y, float floorY, bool fallen) => !fallen && !Fell(l, cell, y, floorY);

    /// <summary>
    /// <paramref name="count"/> cells a body may be set down on (<see cref="SetDownOk"/>) for heroes coming back beside an arrival at
    /// <paramref name="near"/>: distinct, within <paramref name="reach"/> cells, nearest first (ties by (Y, X)), never
    /// <paramref name="near"/> itself (the hero arriving stands there); when too few, the rest share the arrival's own cell (or the
    /// nearest set-down cell anywhere).
    /// </summary>
    public static List<GridPos> SetDownCells(FloorLayout l, GridPos near, int count, int reach = 4)
    {
        var found = new List<(int D, GridPos C)>();
        for (var dy = -reach; dy <= reach; dy++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                var c = new GridPos(near.X + dx, near.Y + dy);
                if (c != near && SetDownOk(l, c)) found.Add((dx * dx + dy * dy, c));
            }
        found.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.C.Y != b.C.Y ? a.C.Y.CompareTo(b.C.Y) : a.C.X.CompareTo(b.C.X));
        var cells = new List<GridPos>(Math.Max(0, count));
        for (var i = 0; i < found.Count && cells.Count < count; i++) cells.Add(found[i].C);
        var spare = SetDownOk(l, near) ? near : NearestSetDown(l, near) ?? near;
        while (cells.Count < count) cells.Add(spare);
        return cells;
    }

    /// <summary>
    /// Where a body is set down near a lead at <paramref name="lead"/> (a bot brought in behind its owner, an escort caught up behind
    /// the lead): <paramref name="spot"/> when its cell may take a body (<see cref="SetDownOk"/>) and, on a floor with drops, the ground
    /// from the lead's own footing (<see cref="NearestStandable"/>) to it holds (<see cref="FootingAlong"/>); else that footing, else
    /// <paramref name="lead"/>. On a floor with no drop it is today's rule: the spot when walkable, else the lead's point.
    /// </summary>
    public static Vector2 SetDownNear(FloorLayout l, float cellSize, Vector2 lead, Vector2 spot)
    {
        if (l.ChasmCount == 0) return l.IsWalkable(CellOf(spot, cellSize)) ? spot : lead;
        var anchor = NearestStandable(l, lead, cellSize) ?? lead;
        return SetDownOk(l, CellOf(spot, cellSize)) && FootingAlong(l, cellSize, anchor, spot) ? spot : anchor;
    }

    /// <summary>
    /// Every walkable cell beside a chasm (a 4-neighbour, a bridge or not; with <paramref name="diagonals"/> the corners too) whose
    /// floor is more than <paramref name="below"/> under that chasm's lip, with the chasm: with 0, a walker there would step into the
    /// shaft's open band; with <see cref="FallDepth"/>, it would be judged fallen at once. The generator never makes a 4-neighbour
    /// one at 0 (the lip-level invariant the sweeps pin); in (Y, X) scan order.
    /// </summary>
    public static IEnumerable<(GridPos Chasm, GridPos Low)> LowGroundBesideDrops(FloorLayout l, bool diagonals = false, float below = 0f)
    {
        for (var y = 0; y < l.Height; y++)
            for (var x = 0; x < l.Width; x++)
            {
                var c = new GridPos(x, y);
                if (!l.IsChasm(c)) continue;
                var lip = l.HeightOf(c);
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || (!diagonals && dx != 0 && dy != 0)) continue;
                        var n = new GridPos(x + dx, y + dy);
                        if (l.IsWalkable(n) && l.HeightOf(n) < lip - below - 0.01f) yield return (c, n);
                    }
            }
    }

    /// <summary>A fall that takes the train spends a spare only while a hero still stands.</summary>
    public static bool SpareAfterFall(int standingHeroes) => standingHeroes > 0;

    /// <summary>
    /// May code set a body down on <paramref name="c"/> (a rider getting out, a set-aside, a formation slot, a teleport)? Walkable
    /// and no drop: a plank is walkable but never a place to put someone.
    /// </summary>
    public static bool SetDownOk(FloorLayout l, GridPos c) => l.IsWalkable(c) && !IsDrop(l, c);

    // ---- the footing of a body the game steers -------------------------------------------------------------------------

    public static GridPos CellOf(Vector2 at, float cellSize) => new((int)MathF.Floor(at.X / cellSize), (int)MathF.Floor(at.Y / cellSize));

    /// <summary>A plank runs along X unless the drop lies east or west of it (<c>ChasmDressing</c> lays its box by this).</summary>
    public static bool PlankRunsAlongX(FloorLayout l, GridPos cell) => !(l.IsChasm(cell + new GridPos(1, 0)) || l.IsChasm(cell + new GridPos(-1, 0)));

    /// <summary>A deck runs along its lane: the step between its neighbours in the party or the enemy lane (X when neither holds it); <c>ChasmDressing</c> lays its box by this.</summary>
    public static bool DeckRunsAlongX(FloorLayout l, GridPos cell)
    {
        if (l.Rail is not { } rail) return true;
        foreach (var lane in new[] { rail.Party, rail.Enemy })
            for (var i = 0; i < lane.Count; i++)
            {
                if (lane[i] != cell) continue;
                var step = lane[Math.Min(lane.Count - 1, i + 1)] - lane[Math.Max(0, i - 1)];
                return Math.Abs(step.X) >= Math.Abs(step.Y);
            }
        return true;
    }

    /// <summary>
    /// The cavern's lane decks, the cells that can give way (<c>ChasmDressing.SetGone</c>): the <see cref="BridgeKind.Deck"/>
    /// party-lane cells of <see cref="RailLine.Cavern"/> in line order, then each one's deck twin on the enemy lane (its
    /// 4-neighbour there) in the same order. Empty on a floor with no cavern.
    /// </summary>
    public static List<GridPos> CavernDecks(FloorLayout l)
    {
        var decks = new List<GridPos>();
        if (l.Rail is not { Cavern: { } span } rail) return decks;
        for (var i = Math.Max(0, span.From); i <= span.To && i < rail.Party.Count; i++)
            if (l.Bridges.TryGetValue(rail.Party[i], out var kind) && kind == BridgeKind.Deck) decks.Add(rail.Party[i]);
        var party = decks.Count;
        for (var k = 0; k < party; k++)
            foreach (var n in decks[k].Neighbours4())
                if (l.IsRail(n) && !l.IsPartyRail(n) && l.Bridges.TryGetValue(n, out var kind) && kind == BridgeKind.Deck) { decks.Add(n); break; }
        return decks;
    }

    /// <summary>
    /// Can a steered body's centre stand at <paramref name="at"/>? Not over a chasm cell, nor over a bridge cell that gave way
    /// (<see cref="FloorLayout.GoneBridges"/>); on a plank's cell only within its boards less <see cref="FootingMargin"/> of its
    /// line; anywhere else (floor, a deck, rock the walls keep it out of) yes.
    /// </summary>
    public static bool Footing(FloorLayout l, Vector2 at, float cellSize)
    {
        var c = CellOf(at, cellSize);
        if (l.IsChasm(c) || (l.GoneBridges.Count > 0 && l.GoneBridges.Contains(c))) return false;
        if (!l.Bridges.TryGetValue(c, out var kind) || kind != BridgeKind.Plank) return true;
        var off = PlankRunsAlongX(l, c) ? at.Y - (c.Y + 0.5f) * cellSize : at.X - (c.X + 0.5f) * cellSize;
        return MathF.Abs(off) <= PlankWidth / 2f - FootingMargin;
    }

    /// <summary>Footing all the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static bool FootingAlong(FloorLayout l, float cellSize, Vector2 a, Vector2 b) => SegmentHolds(l, cellSize, a, b, rockBlocks: false);

    /// <summary>
    /// Every point from <paramref name="a"/> to <paramref name="b"/> has <see cref="Footing"/> (and, with <paramref name="rockBlocks"/>,
    /// none is rock). Footing within one cell is convex (all of it, none of it, or a plank's band), so the segment is judged at both
    /// ends of its run through each cell (just either side of every cell edge it crosses), and every 0.1 m besides.
    /// </summary>
    private static bool SegmentHolds(FloorLayout l, float cellSize, Vector2 a, Vector2 b, bool rockBlocks)
    {
        bool Ok(Vector2 p) => Footing(l, p, cellSize) && (!rockBlocks || l[CellOf(p, cellSize)] is not (Cell.Void or Cell.Wall));
        var d = b - a;
        var length = d.Length();
        if (!Ok(a) || !Ok(b)) return false;
        if (length < 1e-4f) return true;
        const float eps = 1e-3f;
        for (var t = 0.1f / length; t < 1f; t += 0.1f / length) if (!Ok(a + d * t)) return false;
        return Crossings(a.X, d.X) && Crossings(a.Y, d.Y);

        // either side of every cell edge the segment crosses along one axis
        bool Crossings(float from, float delta)
        {
            if (MathF.Abs(delta) < 1e-6f) return true;
            var k0 = (int)MathF.Floor(MathF.Min(from, from + delta) / cellSize);
            var k1 = (int)MathF.Floor(MathF.Max(from, from + delta) / cellSize);
            for (var k = k0 + 1; k <= k1; k++)
            {
                var t = (k * cellSize - from) / delta;
                if (t <= 0f || t >= 1f) continue;
                if (!Ok(a + d * MathF.Max(0f, t - eps / length)) || !Ok(a + d * MathF.Min(1f, t + eps / length))) return false;
            }
            return true;
        }
    }

    /// <summary>A spot a follower may be sent to: a walkable cell, with footing.</summary>
    public static bool Standable(FloorLayout l, Vector2 at, float cellSize) => l.IsWalkable(CellOf(at, cellSize)) && Footing(l, at, cellSize);

    /// <summary>How far (cells, Chebyshev) <see cref="NearestStandable"/> looks round a point.</summary>
    public const int StandSearch = 2;

    /// <summary>
    /// The <see cref="Standable"/> point nearest <paramref name="p"/>: itself when it is one; else, in each walkable cell within
    /// <see cref="StandSearch"/>, the point of it nearest <paramref name="p"/> (on a plank, on its line); null when there is none.
    /// Where a body off its footing heads, and where a follower of a lead off its footing measures from: never a remembered point.
    /// </summary>
    public static Vector2? NearestStandable(FloorLayout l, Vector2 p, float cellSize)
    {
        if (Standable(l, p, cellSize)) return p;
        var here = CellOf(p, cellSize);
        Vector2? best = null;
        var bestD = float.MaxValue;
        const float inset = 0.05f;
        for (var dy = -StandSearch; dy <= StandSearch; dy++)
            for (var dx = -StandSearch; dx <= StandSearch; dx++)
            {
                var c = new GridPos(here.X + dx, here.Y + dy);
                if (!l.IsWalkable(c)) continue;
                var q = new Vector2(Math.Clamp(p.X, c.X * cellSize + inset, (c.X + 1) * cellSize - inset), Math.Clamp(p.Y, c.Y * cellSize + inset, (c.Y + 1) * cellSize - inset));
                if (l.Bridges.TryGetValue(c, out var kind) && kind == BridgeKind.Plank)
                    q = PlankRunsAlongX(l, c) ? q with { Y = (c.Y + 0.5f) * cellSize } : q with { X = (c.X + 0.5f) * cellSize };
                var d = Vector2.DistanceSquared(p, q);
                if (d < bestD && Standable(l, q, cellSize)) { best = q; bestD = d; }
            }
        return best;
    }

    /// <summary>
    /// The planar velocity a steered body at <paramref name="pos"/> may move at when it would move at <paramref name="wish"/>:
    /// the wish while the ground ahead holds (<see cref="Footing"/> all the way out to <see cref="ProbeAhead"/> plus
    /// <see cref="ProbeSeconds"/> of travel, and no rock there, which would turn the step along itself); else the wish along X alone or Z alone (along the lip), or toward the line of the
    /// plank ahead, whichever holds and keeps more of its speed; else standing still. A body already off its footing heads, when
    /// <paramref name="recover"/> (it stands, and is not down), for the nearest point it can stand on (<see cref="NearestStandable"/>),
    /// else stops. On a floor with no chasm, the wish.
    /// </summary>
    public static Vector2 KeepFooting(FloorLayout l, float cellSize, Vector2 pos, Vector2 wish, bool recover)
    {
        if (l.ChasmCount == 0) return wish;
        var speed = wish.Length();
        if (!Footing(l, pos, cellSize))
            return recover && NearestStandable(l, pos, cellSize) is { } back && Vector2.DistanceSquared(back, pos) > 1e-4f ? Vector2.Normalize(back - pos) * MathF.Max(speed, 1.5f) : Vector2.Zero;
        if (speed < 1e-3f || Holds(wish)) return wish;
        var best = Vector2.Zero;
        Consider(new Vector2(wish.X, 0f));
        Consider(new Vector2(0f, wish.Y));
        var ahead = pos + wish / speed * (ProbeAhead + speed * ProbeSeconds);
        var cell = CellOf(ahead, cellSize);
        if (l.Bridges.TryGetValue(cell, out var kind) && kind == BridgeKind.Plank)
        {
            var line = PlankRunsAlongX(l, cell) ? ahead with { Y = (cell.Y + 0.5f) * cellSize } : ahead with { X = (cell.X + 0.5f) * cellSize };
            if (Vector2.DistanceSquared(line, pos) > 1e-4f) Consider(Vector2.Normalize(line - pos) * speed);
        }
        return best;

        void Consider(Vector2 v) { if (v.LengthSquared() > best.LengthSquared() && Holds(v)) best = v; }

        // the whole way ahead holds, and none of it is rock: a wall would turn the step along itself, maybe over the lip
        bool Holds(Vector2 v)
        {
            var len = v.Length();
            return len >= 0.05f && SegmentHolds(l, cellSize, pos, pos + v / len * (ProbeAhead + len * ProbeSeconds), rockBlocks: true);
        }
    }

    /// <summary>
    /// Where a follower walks to keep its formation <paramref name="slot"/> behind a leader at <paramref name="lead"/> facing away
    /// from <paramref name="back"/>. Measured from the leader's own footing (<see cref="NearestStandable"/>: a leader off a plank's
    /// line is taken on it, one over a lip at the floor beside it): the slot itself when it is <see cref="Standable"/> and the
    /// ground from there to it holds; else, that footing on a bridge, in file behind it along the bridge (on a plank's line), the
    /// follower of <paramref name="rank"/> (0 first) <see cref="FileGap"/> × (rank + 1) back, nearer when that does not hold; else
    /// that footing itself. The result always has footing, unless nothing within <see cref="StandSearch"/> cells of the leader does.
    /// </summary>
    public static Vector2 FollowSpot(FloorLayout l, float cellSize, Vector2 lead, Vector2 back, Vector2 slot, int rank)
    {
        var anchor = NearestStandable(l, lead, cellSize) ?? lead;
        if (Standable(l, slot, cellSize) && FootingAlong(l, cellSize, anchor, slot)) return slot;
        var cell = CellOf(anchor, cellSize);
        if (!l.Bridges.TryGetValue(cell, out var kind)) return anchor;
        var alongX = kind == BridgeKind.Plank ? PlankRunsAlongX(l, cell) : DeckRunsAlongX(l, cell);
        var axis = alongX ? new Vector2(back.X < 0f ? -1f : 1f, 0f) : new Vector2(0f, back.Y < 0f ? -1f : 1f);
        var from = kind != BridgeKind.Plank ? anchor : alongX ? anchor with { Y = (cell.Y + 0.5f) * cellSize } : anchor with { X = (cell.X + 0.5f) * cellSize };
        for (var depth = FileGap * (Math.Max(0, rank) + 1); depth > 0.25f; depth -= 0.5f)
        {
            var p = from + axis * depth;
            if (Standable(l, p, cellSize) && FootingAlong(l, cellSize, from, p)) return p;
        }
        return from;
    }

    // ---- the walking warning -------------------------------------------------------------------------------------------

    /// <summary>
    /// The 4-connected regions of chasm-or-bridge cells, numbered in (Y, X) scan order as <c>ChasmDressing</c> finds them: each
    /// drop cell's region index.
    /// </summary>
    public static Dictionary<GridPos, int> Regions(FloorLayout l)
    {
        var region = new Dictionary<GridPos, int>();
        var next = 0;
        for (var y = 0; y < l.Height; y++)
            for (var x = 0; x < l.Width; x++)
            {
                var start = new GridPos(x, y);
                if (!IsDrop(l, start) || region.ContainsKey(start)) continue;
                var queue = new Queue<GridPos>();
                region[start] = next;
                queue.Enqueue(start);
                while (queue.Count > 0)
                    foreach (var n in queue.Dequeue().Neighbours4())
                        if (IsDrop(l, n) && region.TryAdd(n, next)) queue.Enqueue(n);
                next++;
            }
        return region;
    }

    /// <summary>The regions (<see cref="Regions"/>) within <see cref="WarnCells"/> (Chebyshev) of <paramref name="cell"/>, lowest index first.</summary>
    public static IEnumerable<int> RegionsNear(IReadOnlyDictionary<GridPos, int> regions, GridPos cell)
    {
        var near = new SortedSet<int>();
        for (var dy = -WarnCells; dy <= WarnCells; dy++)
            for (var dx = -WarnCells; dx <= WarnCells; dx++)
                if (regions.TryGetValue(new GridPos(cell.X + dx, cell.Y + dy), out var r)) near.Add(r);
        return near;
    }

    /// <summary>The first region within <see cref="WarnCells"/> of <paramref name="cell"/> (row by row) not yet in <paramref name="warned"/>, or −1. Allocates nothing.</summary>
    public static int UnwarnedNear(IReadOnlyDictionary<GridPos, int> regions, GridPos cell, HashSet<int> warned)
    {
        for (var dy = -WarnCells; dy <= WarnCells; dy++)
            for (var dx = -WarnCells; dx <= WarnCells; dx++)
                if (regions.TryGetValue(new GridPos(cell.X + dx, cell.Y + dy), out var r) && !warned.Contains(r)) return r;
        return -1;
    }
}
