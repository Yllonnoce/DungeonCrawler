using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using Xunit;

namespace DungeonCrawler.Tests.Dungeon;

/// <summary>A Shifting Vault floor always has a solvable, dead-end-free puzzle (or an honest fallback), the same every time.</summary>
public class VaultPuzzleTests
{
    public static IEnumerable<object[]> KindsAndSeeds()
    {
        foreach (var kind in new[] { LayoutKind.Standard, LayoutKind.Ring, LayoutKind.Mine, LayoutKind.Flooded, LayoutKind.Burning })
            for (var i = 0; i < 30; i++) yield return new object[] { kind, i * 7919 + 13 };
    }

    private static FloorLayout Gen(LayoutKind kind, int seed) => FloorGenerator.Generate(FloorSpec.Default with { Layout = kind, Objective = FloorObjectiveKind.ShiftingVault }, seed);

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void Vault_SealsTheStairs_AndPuzzleIsProven(LayoutKind kind, int seed)
    {
        var layout = Gen(kind, seed);
        if (layout.Objective == null) return; // nowhere to seal the stairs: the floor gave the objective up honestly (its stairs stay unlocked)
        var objective = layout.Objective;
        Assert.Equal(FloorObjectiveKind.ShiftingVault, objective.Kind);
        Assert.True(objective.StairsVault >= 0, "the stairs are not in a vault");
        var vault = layout.Vaults[objective.StairsVault];
        Assert.True(vault.Room.Contains(layout.DownStairs));
        Assert.Equal(VaultKind.LeverGate, vault.Kind);
        Assert.True(vault.Revealed);
        Assert.Equal(Cell.Door, layout[vault.Door]);
        Assert.Equal(Cell.Floor, layout[layout.DownStairs]);
        // the stairs are reachable with the vault door open and sealed off with it shut
        Assert.Contains(layout.DownStairs, layout.Reachable(layout.UpStairs));
        Assert.DoesNotContain(layout.DownStairs, layout.Reachable(layout.UpStairs, p => p == vault.Door));
        // no enemy stands inside the vault, nothing else sits on the door
        Assert.DoesNotContain(layout.EnemySpawns, s => vault.Room.Contains(s));

        if (objective.Puzzle is not { } puzzle)
        {
            Assert.NotNull(vault.Lever); // the honest fallback: an ordinary lever somewhere
            Assert.Contains(vault.Lever!.Value, layout.Reachable(layout.UpStairs, p => p == vault.Door));
            return;
        }
        Assert.Null(vault.Lever);
        Assert.InRange(puzzle.GateCount, 1, VaultPuzzles.MaxGates);
        Assert.InRange(puzzle.LeverCount, 1, VaultPuzzles.MaxLevers);
        Assert.NotEqual(puzzle.Initial, puzzle.Target);
        var all = (1 << puzzle.GateCount) - 1;
        Assert.Equal(all, puzzle.Toggles.Aggregate(0, (a, b) => a | b)); // every gate answers to a lever
        Assert.All(puzzle.Toggles, t => Assert.NotEqual(0, t));          // every lever moves something
        Assert.True(VaultPuzzles.Verify(layout, puzzle, vault.Door, vault.Room));
        // gates stand in corridors, levers in distinct ordinary rooms, all reserved
        foreach (var g in puzzle.Gates) { Assert.Null(layout.RoomAt(g)); Assert.Equal(Cell.Floor, layout[g]); Assert.True(layout.IsReserved(g)); }
        var rooms = puzzle.Levers.Select(l => layout.RoomAt(l)).ToList();
        Assert.All(rooms, r => Assert.NotNull(r));
        Assert.Equal(rooms.Count, rooms.Distinct().Count());
        Assert.DoesNotContain(rooms, r => r == vault.Room);
        foreach (var l in puzzle.Levers) Assert.DoesNotContain(l, layout.EnemySpawns);
        // a plaque beside every lever and one notice in the start room
        Assert.Equal(puzzle.LeverCount + 1, objective.Plaques.Count);
        Assert.Contains(objective.Plaques, p => p.Clue < 0 && layout.RoomAt(p.Cell) != null);
        foreach (var (cell, clue) in objective.Plaques)
        {
            Assert.True(layout.IsReserved(cell));
            if (clue >= 0) Assert.InRange(Math.Abs(cell.X - puzzle.Levers[clue].X) + Math.Abs(cell.Y - puzzle.Levers[clue].Y), 1, 4);
        }
        Assert.Contains("Gate I", VaultPuzzles.VaultClue(puzzle, layout));
        Assert.Contains("This lever turns", VaultPuzzles.LeverClue(puzzle, 0));
    }

    [Theory, MemberData(nameof(KindsAndSeeds))]
    public void Deterministic(LayoutKind kind, int seed)
    {
        var a = Gen(kind, seed);
        var b = Gen(kind, seed);
        Assert.Equal(a.ContentHash(), b.ContentHash());
        Assert.Equal(a.Objective?.Puzzle?.Toggles, b.Objective?.Puzzle?.Toggles);
        Assert.Equal(a.Objective?.Puzzle?.Target, b.Objective?.Puzzle?.Target);
    }

    [Fact]
    public void MostFloors_GetARealPuzzle()
    {
        var real = 0;
        for (var seed = 1; seed <= 40; seed++) if (Gen(LayoutKind.Standard, seed).Objective?.Puzzle != null) real++;
        Assert.True(real >= 36, $"only {real}/40 standard floors got a puzzle");
    }

    [Fact]
    public void Verify_RejectsADeadEnd()
    {
        // one gate on the only way to the vault, one lever behind that gate: shut it and you can never open it again
        var layout = Enumerable.Range(1, 30).Select(s => Gen(LayoutKind.Standard, s)).First(l => l.Objective is { StairsVault: >= 0 });
        var vault = layout.Vaults[layout.Objective!.StairsVault];
        var doorstep = VaultPuzzles.Doorstep(layout, vault.Door, vault.Room)!.Value;
        // a gate right on the doorstep with its lever inside the vault: unreachable once shut
        var bad = new VaultPuzzle(new[] { vault.Room.Center }, new[] { doorstep }, new[] { 1 }, Initial: 0, Target: 1);
        Assert.False(VaultPuzzles.Verify(layout, bad, vault.Door, vault.Room));
    }

    [Fact]
    public void StateFor_FlipsBits()
    {
        var p = new VaultPuzzle(new GridPos[3], new GridPos[3], new[] { 0b011, 0b110, 0b100 }, Initial: 0b001, Target: 0b111);
        Assert.Equal(0b001, p.StateFor(0));
        Assert.Equal(0b010, p.StateFor(1));
        Assert.Equal(0b111, p.StateFor(0b010));
        Assert.Equal(0b100, p.StateFor(0b011)); // levers 0 and 1 together
        Assert.Equal(p.StateFor(0b011), p.StateFor(0b011 ^ 0b100 ^ 0b100)); // a lever pulled twice is not pulled
        Assert.True(p.IsOpen(0b100, 2));
        Assert.False(p.IsOpen(0b100, 0));
    }
}
