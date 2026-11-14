using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Objectives;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-vault-puzzle -----------------------------------------------------------------------------------
// Every Shifting Vault floor of this game's campaign: build its layout the way the game does, print the puzzle, its clues
// and the shortest lever order that opens it (with and without hidden passages), then go to each floor and pull that order
// on the real levers. Before that, every floor of the story and of many seeded runs goes through FloorSolvability (every
// lock's opener in reach, the way down open). Prints [vault] lines and [vault] RESULT.
public partial class DebugTools
{
    private bool _testVault;
    private const int VaultSweepSeeds = 150;
    private int _vaultStep;
    private double _vaultAt;
    private readonly List<int> _vaultFloors = new();
    private readonly List<string> _vaultFails = new();

    private void RunVaultPuzzleTest()
    {
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout) return;
        if (_elapsed < _vaultAt) return;
        var game = Game.Instance;
        if (_vaultStep == 0)
        {
            _vaultStep = 1;
            GD.Print($"[vault] campaign \"{game.Adventure.Campaign?.Title ?? "(none)"}\" seed={game.GameSeed}");
            for (var f = 2; f <= TierRules.FinalFloor; f++)
            {
                var spec = main.Builder.SpecFor(f);
                if (spec.Objective != FloorObjectiveKind.ShiftingVault) continue;
                var probe = FloorGenerator.Generate(spec, Rng.FloorSeed(game.GameSeed, f));
                if (probe.Objective is not { Kind: FloorObjectiveKind.ShiftingVault } objective) { GD.Print($"[vault] floor {f}: the shape gave the objective up (stairs open)"); continue; }
                var vault = probe.Vaults[objective.StairsVault];
                if (objective.Puzzle is not { } puzzle) { GD.Print($"[vault] floor {f}: no puzzle, one ordinary lever at {vault.Lever}"); _vaultFloors.Add(f); continue; }
                var proven = VaultPuzzles.Verify(probe, puzzle, vault.Door, vault.Room);
                var provenHidden = VaultPuzzles.Verify(probe, puzzle, vault.Door, vault.Room, secretsOpen: false);
                var order = VaultPuzzles.Solve(probe, puzzle, vault.Door, vault.Room, 0, probe.UpStairs);
                var orderHidden = VaultPuzzles.Solve(probe, puzzle, vault.Door, vault.Room, 0, probe.UpStairs, secretsOpen: false);
                GD.Print($"[vault] floor {f}: {puzzle.GateCount} gates, {puzzle.LeverCount} levers, plaques={objective.Plaques.Count} proven={proven} provenWithoutSecrets={provenHidden}");
                for (var g = 0; g < puzzle.GateCount; g++)
                    GD.Print($"[vault]   {VaultPuzzles.GateName(g)} at {puzzle.Gates[g]} ({VaultPuzzles.Direction(probe, puzzle.Gates[g])}): starts {(puzzle.IsOpen(puzzle.Initial, g) ? "open" : "shut")}, wanted {(puzzle.IsOpen(puzzle.Target, g) ? "open" : "shut")}");
                for (var i = 0; i < puzzle.LeverCount; i++)
                    GD.Print($"[vault]   lever {i} at {puzzle.Levers[i]} ({VaultPuzzles.Direction(probe, puzzle.Levers[i])}, room {probe.Rooms.IndexOf(probe.RoomAt(puzzle.Levers[i])!)}): {VaultPuzzles.LeverClue(puzzle, i)}");
                GD.Print($"[vault]   notice: {VaultPuzzles.VaultClue(puzzle, probe)}");
                GD.Print($"[vault]   solution: {(order == null ? "NONE" : string.Join(" then ", order.Select(i => $"lever {i}")))}; without hidden passages: {(orderHidden == null ? "NONE" : string.Join(" then ", orderHidden.Select(i => $"lever {i}")))}");
                if (!proven || order == null) _vaultFails.Add($"floor {f} unsolvable");
                if (!provenHidden || orderHidden == null) _vaultFails.Add($"floor {f} needs a hidden passage");
                _vaultFloors.Add(f);
            }
            // every floor of the story, then every floor of many seeded runs with no story (the campaign set aside while they are dealt)
            Sweep("story", new[] { game.GameSeed });
            var campaign = game.Adventure.Campaign;
            game.Adventure.Campaign = null;
            try { Sweep("random runs", Enumerable.Range(1, VaultSweepSeeds).Select(i => Rng.Mix((ulong)i * 0x9E3779B97F4A7C15UL))); }
            finally { game.Adventure.Campaign = campaign; }
            if (_vaultFloors.Count == 0) { Finish(); return; }
            game.JumpToFloor(_vaultFloors[0]);
            _vaultAt = _elapsed + 2.0;
            return;

            void Sweep(string what, IEnumerable<ulong> seeds)
            {
                int floors = 0, objectives = 0, bad = 0;
                foreach (var seed in seeds)
                    for (var f = 1; f <= TierRules.FinalFloor; f++)
                    {
                        var spec = main.Builder.SpecFor(f, seed);
                        var probe = FloorGenerator.Generate(spec, Rng.FloorSeed(seed, f));
                        floors++;
                        if (probe.Objective != null) objectives++;
                        foreach (var p in FloorSolvability.Problems(probe))
                        {
                            bad++;
                            var line = $"{what}: seed {seed} floor {f} ({spec.Layout}, {probe.Objective?.Kind.ToString() ?? "no objective"}): {p}";
                            GD.Print($"[vault] PROBLEM {line}");
                            _vaultFails.Add(line);
                        }
                        if (FloorSolvability.Problems(probe).Count > 0)
                            foreach (var v in probe.Vaults)
                            {
                                var at = v.Key ?? v.Lever;
                                GD.Print($"[vault]   vault {v.Index} {v.Kind} door {v.Door} room {v.Room.X},{v.Room.Y} {v.Room.W}x{v.Room.H} opener {at}"
                                    + (at is { } c ? $": {probe[c]} level {probe.LevelOf(c)} under {probe.IsUnder(c)} water {probe.IsWater(c)} cellar {probe.Cellars.Any(x => x.Cells.Contains(c) || x.Pit == c)} in vault {string.Join(",", probe.Vaults.Where(w => w.Room.Contains(c)).Select(w => w.Index))} reach(all open) {probe.Reachable(probe.UpStairs).Contains(c)}" : ""));
                            }
                    }
                GD.Print($"[vault] {what}: {floors} floors checked, {objectives} with an objective, {bad} problems");
            }
        }
        // on a vault floor: pull the shortest order from where the party stands and watch the vault open
        var floor = game.CurrentFloor;
        if (ObjectiveController.Instance is not ShiftingVaultController ctrl) { _vaultFails.Add($"floor {floor}: no Shifting Vault controller"); }
        else
        {
            var party = main.Party;
            var objective = layout.Objective!;
            var vault = layout.Vaults[objective.StairsVault];
            var vaultDoor = root.GetChildren().OfType<Door>().First(d => d.VaultIndex == objective.StairsVault);
            var stairs = root.GetNode<Stairs>("DownStairs");
            var lockedBefore = stairs.Locked;
            if (ctrl.Puzzle is { } puzzle)
            {
                var order = VaultPuzzles.Solve(layout, puzzle, vault.Door, vault.Room, ctrl.Parity, DungeonBuilder.WorldToCell(party.Lead.GlobalPosition));
                if (order == null) _vaultFails.Add($"floor {floor}: no order from where the party stands");
                else foreach (var i in order) ctrl.Levers[i].Interact(party, party.Lead);
                GD.Print($"[vault] floor {floor}: pulled {(order == null ? "nothing" : string.Join(",", order))}: {ctrl.Right}/{puzzle.GateCount} right, vault open={vaultDoor.IsOpen}, completed={ctrl.Completed}, stairs locked {lockedBefore} -> {stairs.Locked}");
            }
            else
            {
                root.GetChildren().OfType<Lever>().FirstOrDefault(l => l.VaultIndex == objective.StairsVault)?.Interact(party, party.Lead);
                GD.Print($"[vault] floor {floor}: ordinary lever pulled, vault open={vaultDoor.IsOpen}");
            }
            if (!vaultDoor.IsOpen || !ctrl.Completed) _vaultFails.Add($"floor {floor}: the vault stayed shut");
        }
        _vaultFloors.RemoveAt(0);
        if (_vaultFloors.Count == 0) { Finish(); return; }
        game.JumpToFloor(_vaultFloors[0]);
        _vaultAt = _elapsed + 2.0;

        void Finish()
        {
            GD.Print($"[vault] RESULT {(_vaultFails.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", _vaultFails))}");
            _testVault = false;
            GetTree().Quit(_vaultFails.Count == 0 ? 0 : 1);
        }
    }
}
