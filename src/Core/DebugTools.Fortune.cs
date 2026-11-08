using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Generation;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Core;

// ---- --test-fortune ---------------------------------------------------------------------------------------
// Fortune and misfortune end to end: floor 2's shrine and card (a boon's stat, one use), a gold price refused when broke and
// paid, a blood price, a curse laid and lifted, the vault table's odds, a forced mimic (save and load mid-spring, the yield),
// the bound chest's hunt, a secret room found once, the third lockpick jamming (a key opens it; the jam survives a save),
// the Lucky Dip (win or dud, refusals, the tab's path) and a save round trip. Prints [fortune] lines and [fortune] RESULT.
public partial class DebugTools
{
    private bool _testFortune;
    private int _ftStep;
    private double _ftAt;
    private readonly List<string> _ftFails = new();
    private readonly List<string> _ftLog = new();
    private bool _ftSubscribed;
    private int _ftMimicSprung, _ftSecretFound, _ftGambled, _ftBroke;
    private int _ftMimicIndex = -1, _ftMimicFloor = -1, _ftBoundFloor = -1, _ftLockIndex = -1, _ftSecretFloor = -1, _ftSecretIndex = -1, _ftMineFloor = -1, _ftAmbusherIndex = -1;
    private int _ftEnemiesBefore;
    private float _ftArmorBase, _ftPerceptionBase, _ftArmorBeforeSave;
    private int _ftGoldBefore;
    private Enemy? _ftSleeper;
    private List<string> _ftBoonsBefore = new(), _ftCursesBefore = new();
    private List<int> _ftDipBefore = new();
    private bool _ftShrineUsedBefore;
    private int _ftPicks;

    private void RunFortuneTest()
    {
        if (_ftStep >= 99) return;
        var main = Main.Instance;
        void Fail(string why) { _ftFails.Add(why); GD.Print($"[fortune] FAIL: {why}"); }
        void Finish()
        {
            GD.Print(_ftFails.Count == 0 ? "[fortune] RESULT ok" : $"[fortune] RESULT FAIL ({string.Join("; ", _ftFails)})");
            _ftStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        if (_elapsed > 190) { Fail($"timed out at step {_ftStep}"); Finish(); return; }
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        if (_elapsed < _ftAt) return;
        var party = main.Party;
        var b = main.Builder;
        var cat = Game.Instance.Catalog;
        var adv = Game.Instance.Adventure;
        var floor = b.CurrentFloor;
        void Next(double wait = 0.2) { _ftStep++; _ftAt = _elapsed + wait; }
        void Jump(int to, double wait = 1.2) { Game.Instance.JumpToFloor(to); _ftStep++; _ftAt = _elapsed + wait; }
        void Quiet() { foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree(); }
        Vector3 Stand(GridPos cell) => b.Surface(cell) + Vector3.Up * 0.3f;
        Vector3 Beside(Node3D node) => node.GlobalPosition + new Vector3(1.4f, 0.3f, 0f);
        Shrine? ShrineNode() => root.GetChildren().OfType<Shrine>().FirstOrDefault();
        Chest? ChestAt(int index) => root.GetChildren().OfType<Chest>().FirstOrDefault(c => c.SpawnIndex == index);
        bool Logged(string part) => _ftLog.Any(l => l.Contains(part, StringComparison.OrdinalIgnoreCase));
        FloorLayout Preview(int f) => FloorGenerator.Generate(b.SpecFor(f), Rng.FloorSeed(Game.Instance.GameSeed, f));
        var card = main.Hud.Shrine;
        if (!_ftSubscribed)
        {
            _ftSubscribed = true;
            EventBus.Instance.Message += l => _ftLog.Add(l);
            EventBus.Instance.MimicSprung += _ => _ftMimicSprung++;
            EventBus.Instance.SecretFound += _ => _ftSecretFound++;
            EventBus.Instance.Gambled += (_, _) => _ftGambled++;
            EventBus.Instance.BrokeAtShop += _ => _ftBroke++;
        }

        switch (_ftStep)
        {
            case 0 when floor == 2 && _elapsed >= 1.0:
            {
                // 1. floor 2: the shrine, its card, a boon's stat, one use
                Quiet();
                var shrine = ShrineNode();
                GD.Print($"[fortune] floor 2 seed {Game.Instance.GameSeed}: shrine cell {layout.ShrineCell?.ToString() ?? "none"}, node {(shrine != null ? $"at {DungeonBuilder.WorldToCell(shrine.GlobalPosition)} price {shrine.Price} prompt \"{shrine.Prompt}\"" : "missing")}, card open={main.Hud.ShrineOpen}");
                if (layout.ShrineCell == null) { Fail("floor 2 has no shrine cell"); Finish(); return; }
                if (shrine == null) { Fail("no Shrine node on floor 2"); Finish(); return; }
                if (DungeonBuilder.WorldToCell(shrine.GlobalPosition) != layout.ShrineCell) Fail("the shrine stands off its cell");
                if (main.Hud.ShrineOpen) Fail("the card is open before anyone touched the altar");
                if (shrine.Price != ShrineRules.Price.None) { ShrineRules.ForcedPrice = ShrineRules.Price.None; GD.Print("[fortune] (seed gave a priced shrine on floor 2; the first check wants a free one, so the price is paid as asked)"); }
                party.AddGold(1000); // whatever floor 2 asks, the party can pay
                party.Teleport(Beside(shrine), shrine.GlobalPosition);
                _ftArmorBase = party.Members[0].Stats[Stat.Armor];
                _ftPerceptionBase = party.Members[0].Stats[Stat.Perception];
                shrine.Interact(party, party.Lead);
                Next(0.5);
                break;
            }
            case 1:
            {
                var shrine = ShrineNode()!;
                GD.Print($"[fortune] after the touch: card open={main.Hud.ShrineOpen} rows={card.RowCount} [{string.Join(" | ", Enumerable.Range(0, card.RowCount).Select(card.RowText))}] boons={adv.Boons.Count} used={shrine.Used}");
                if (!main.Hud.ShrineOpen) { Fail("the card did not open within 0.5 s"); Finish(); return; }
                if (card.RowCount < 3) Fail($"the card has {card.RowCount} rows, want at least 3");
                FortuneDirector.Instance!.TakeBoon("thick_hide", 0);
                var armor = party.Members[0].Stats[Stat.Armor];
                GD.Print($"[fortune] Thick Hide taken directly: armour {_ftArmorBase:0.##} -> {armor:0.##}, boons [{string.Join(",", adv.Boons)}]");
                if (Math.Abs(armor - (_ftArmorBase + 12f)) > 0.01f) Fail($"Thick Hide gave {armor - _ftArmorBase:0.##} armour, want 12");
                if (!adv.Boons.Contains("thick_hide")) Fail("thick_hide is not in Adventure.Boons");
                var pick = Enumerable.Range(0, card.Card!.Boons.Count).First(i => card.Card.Boons[i] != "thick_hide");
                card.Pick(pick);
                GD.Print($"[fortune] picked row {pick} ({card.Card?.Boons.ElementAtOrDefault(pick) ?? "closed"}): card open={main.Hud.ShrineOpen} boons={adv.Boons.Count} used={shrine.Used} prompt \"{shrine.Prompt}\"");
                if (main.Hud.ShrineOpen) Fail("the card stayed open after the pick");
                if (adv.Boons.Count != 2) Fail($"{adv.Boons.Count} boons after the pick, want 2");
                if (!shrine.Used) Fail("the shrine is not used after the pick");
                if (shrine.Prompt != "Cold altar") Fail($"the used shrine's prompt is \"{shrine.Prompt}\"");
                shrine.Interact(party, party.Lead);
                if (main.Hud.ShrineOpen || adv.Boons.Count != 2) Fail("a second touch did something");
                // 2. a gold price, refused when broke
                ShrineRules.ForcedPrice = ShrineRules.Price.Gold;
                party.SetGold(0);
                Jump(3);
                break;
            }
            case 2 when floor == 3:
            {
                Quiet();
                var shrine = ShrineNode();
                if (shrine == null) { Fail("floor 3 has no shrine"); ShrineRules.ForcedPrice = ShrineRules.Price.Blood; Jump(4); return; }
                party.Teleport(Beside(shrine), shrine.GlobalPosition);
                _ftLog.Clear();
                shrine.Interact(party, party.Lead);
                GD.Print($"[fortune] floor 3 gold price {ShrineRules.GoldPrice(3)} with 0 gold: used={shrine.Used} card={main.Hud.ShrineOpen} log [{string.Join(" | ", _ftLog)}]");
                if (!Logged("you do not have")) Fail("the broke party was not refused");
                if (shrine.Used || main.Hud.ShrineOpen) Fail("the refused shrine was used or opened");
                party.AddGold(ShrineRules.GoldPrice(3));
                shrine.Interact(party, party.Lead);
                GD.Print($"[fortune] paid: gold={party.Gold} used={shrine.Used} card={main.Hud.ShrineOpen}");
                if (party.Gold != 0) Fail($"gold is {party.Gold} after paying the price, want 0");
                if (!main.Hud.ShrineOpen) Fail("the card did not open after paying");
                card.Pick(-1);
                GD.Print($"[fortune] walked away: gold={party.Gold} (skip pays {ShrineRules.SkipGold(3)}) card={main.Hud.ShrineOpen}");
                if (party.Gold != ShrineRules.SkipGold(3)) Fail($"the skip paid {party.Gold}, want {ShrineRules.SkipGold(3)}");
                // 3. a blood price
                ShrineRules.ForcedPrice = ShrineRules.Price.Blood;
                Jump(4);
                break;
            }
            case 3 when floor == 4:
            {
                Quiet();
                var shrine = ShrineNode();
                if (shrine == null) { Fail("floor 4 has no shrine"); ShrineRules.ForcedPrice = ShrineRules.Price.None; Jump(5); return; }
                foreach (var m in party.Members) if (m.IsAlive) m.Hp = m.Stats[Stat.MaxHp];
                party.Teleport(Beside(shrine), shrine.GlobalPosition);
                shrine.Interact(party, party.Lead);
                var hp = string.Join(", ", party.Members.Where(m => m.IsAlive).Select(m => $"{m.Name} {m.Hp:0.#}/{m.Stats[Stat.MaxHp]:0.#}"));
                GD.Print($"[fortune] floor 4 blood price: {hp}; card={main.Hud.ShrineOpen}");
                foreach (var m in party.Members.Where(m => m.IsAlive))
                    if (Math.Abs(m.Hp - m.Stats[Stat.MaxHp] * 0.75f) > 1f) Fail($"{m.Name} has {m.Hp:0.#} of {m.Stats[Stat.MaxHp]:0.#} after the blood price, want three quarters");
                // the card is left open across the floor change: the next floor must find it closed and nothing pending (a wipe mid-card rebuilds the floor the same way)
                // 4. a curse laid directly, then lifted at the next altar (any curse an earlier altar laid is cleared first: the lift row names the first one held)
                if (adv.Curses.Count > 0) { GD.Print($"[fortune] clearing [{string.Join(",", adv.Curses)}] so Dim is the one curse"); foreach (var c in adv.Curses.ToList()) FortuneRules.Lift(adv, c); FortuneDirector.Instance!.Refresh(); _ftPerceptionBase = party.Members[0].Stats[Stat.Perception]; }
                FortuneDirector.Instance!.TakeCurse("dim", 0);
                var perception = party.Members[0].Stats[Stat.Perception];
                GD.Print($"[fortune] Dim taken: perception {_ftPerceptionBase:0.##} -> {perception:0.##}, curses [{string.Join(",", adv.Curses)}]");
                if (Math.Abs(perception - (_ftPerceptionBase - 20f)) > 0.01f) Fail($"Dim took {_ftPerceptionBase - perception:0.##} perception, want 20");
                ShrineRules.ForcedPrice = ShrineRules.Price.None;
                Jump(5);
                break;
            }
            case 4 when floor == 5:
            {
                Quiet();
                GD.Print($"[fortune] floor 5 with floor 4's card left open: card open={main.Hud.ShrineOpen} pending={(FortuneDirector.Instance!.PendingShrine != null)}");
                if (main.Hud.ShrineOpen) { Fail("the card stayed open across a floor change (a soft-lock: input stays locked)"); card.Close("gone"); }
                if (FortuneDirector.Instance.PendingShrine != null) Fail("a card is still pending after a floor change");
                var shrine = ShrineNode();
                if (shrine == null) { Fail("floor 5 has no shrine"); Next(); return; }
                party.SetGold(0);
                party.AddGold(ShrineRules.LiftPrice(5));
                party.Teleport(Beside(shrine), shrine.GlobalPosition);
                shrine.Interact(party, party.Lead);
                var rows = string.Join(" | ", Enumerable.Range(0, card.RowCount).Select(card.RowText));
                GD.Print($"[fortune] floor 5 lift: card={main.Hud.ShrineOpen} rows [{rows}]");
                if (!Enumerable.Range(0, card.RowCount).Any(i => card.RowText(i).Contains("Dim"))) Fail("the card offers no row to lift Dim");
                card.Pick(-2);
                var perception = party.Members[0].Stats[Stat.Perception];
                GD.Print($"[fortune] lifted: curses [{string.Join(",", adv.Curses)}] gold={party.Gold} perception={perception:0.##} card={main.Hud.ShrineOpen}");
                if (adv.Curses.Count != 0) Fail("Dim was not lifted");
                if (party.Gold != 0) Fail($"gold is {party.Gold} after lifting, want 0 (the lift costs {ShrineRules.LiftPrice(5)})");
                if (Math.Abs(perception - _ftPerceptionBase) > 0.01f) Fail("perception did not come back after the lift");
                if (main.Hud.ShrineOpen) Fail("the card stayed open after the lift");
                Next();
                break;
            }
            case 5:
            {
                // 5. the vault chest and the vault table's odds
                var vaults = layout.Vaults.Where(v => v.Kind != VaultKind.SafeDoor).ToList();
                var vaultChests = root.GetChildren().OfType<Chest>().Where(c => c.TableId == ChestRules.VaultTable && !c.Bound).ToList();
                GD.Print($"[fortune] floor {floor}: {vaults.Count} vault(s), {vaultChests.Count} chest(s) on table.vault, bound chest {layout.BoundChest}");
                if (vaults.Count > 0 && vaultChests.Count == 0) Fail("a vault room's chest does not roll table.vault");
                if (vaults.Count == 0) GD.Print("[fortune] no vault on this floor");
                var rng = new Random(1);
                var commons = Enumerable.Range(0, 20).SelectMany(_ => LootRoller.RollMany(cat, cat.Table(ChestRules.VaultTable), 5, rng)).Count(i => i.IsEquippable && i.Rarity == Rarity.Common);
                GD.Print($"[fortune] twenty vault rolls: {commons} common equippables");
                if (commons > 0) Fail($"the vault table rolled {commons} common equippables");
                // 6. the mimic: forced before its floor is built (an ordinary chest that is not the bound one; a lock on a mouth is part of the lure)
                for (var f = 6; f <= 9 && _ftMimicIndex < 0; f++)
                {
                    var l = Preview(f);
                    var i = Enumerable.Range(0, l.ChestSpawns.Count).FirstOrDefault(i => ChestRules.KindOf(l, i) == ChestKind.Ordinary && l.BoundChest != i, -1);
                    if (i >= 0) { _ftMimicIndex = i; _ftMimicFloor = f; }
                }
                if (_ftMimicIndex < 0) { Fail("no floor in 6..9 has an ordinary chest to force a mimic on"); _ftStep = 10; _ftAt = _elapsed; return; }
                MimicRules.ForcedSpawnIndex = _ftMimicIndex;
                GD.Print($"[fortune] forcing chest {_ftMimicIndex} on floor {_ftMimicFloor} to be a mimic");
                Jump(_ftMimicFloor);
                break;
            }
            case 6 when floor == _ftMimicFloor:
            {
                Quiet();
                var chest = ChestAt(_ftMimicIndex);
                if (chest == null) { Fail($"chest {_ftMimicIndex} is missing on floor {floor}"); _ftStep = 10; _ftAt = _elapsed; return; }
                party.Teleport(Beside(chest), chest.GlobalPosition);
                Next(0.3);
                break;
            }
            case 7:
            {
                var chest = ChestAt(_ftMimicIndex)!;
                GD.Print($"[fortune] floor {floor} chest {_ftMimicIndex}: mimic={chest.IsMimic} prompt \"{chest.Prompt}\" (lead perception {party.Lead.State.Stats[Stat.Perception]:0}, {party.Lead.GlobalPosition.DistanceTo(chest.GlobalPosition):0.0} m)");
                if (!chest.IsMimic) Fail("the forced chest is not a mimic");
                if (!chest.Prompt.Contains(MimicRules.Tell)) Fail("a member beside the chest does not read the breathing lock");
                _ftEnemiesBefore = root.GetChildren().OfType<Enemy>().Count(e => IsInstanceValid(e) && !e.IsQueuedForDeletion());
                var sprungBefore = _ftMimicSprung;
                chest.Interact(party, party.Lead);
                var enemies = root.GetChildren().OfType<Enemy>().Where(e => IsInstanceValid(e) && !e.IsQueuedForDeletion()).ToList();
                var brute = enemies.FirstOrDefault(e => e.DisplayName == MimicRules.EliteName);
                var baseXp = brute != null ? b.EnemyProfile(brute.Profile.Id)?.Xp ?? 0 : 0;
                GD.Print($"[fortune] sprung: enemies {_ftEnemiesBefore} -> {enemies.Count}, brute {(brute != null ? $"{brute.Profile.Id} \"{brute.DisplayName}\" xp {brute.Profile.Xp} (base {baseXp}) hp {brute.MaxHp:0} scale {brute.Profile.Scale:0.00} unaware={brute.IsUnaware}" : "none")}, chest sprung={chest.Sprung} opened={chest.Opened}, MimicSprung fired {_ftMimicSprung - sprungBefore}");
                if (enemies.Count != _ftEnemiesBefore + 1) Fail($"the spring added {enemies.Count - _ftEnemiesBefore} enemies, want 1");
                if (brute == null) Fail("no enemy named Mimic after the spring");
                else
                {
                    if (brute.Profile.Xp != baseXp * 2) Fail($"the mimic's XP is {brute.Profile.Xp}, want twice {baseXp}");
                    if (brute.IsUnaware) Fail("the sprung mimic is not hunting");
                }
                if (!chest.Sprung || chest.Opened) Fail("the chest is not sprung-and-unopened");
                if (_ftMimicSprung - sprungBefore != 1) Fail("MimicSprung did not fire once");
                party.Teleport(Stand(layout.UpStairs)); // away from the chest, so the companions do not kill the restored brute before it is looked at
                main.SaveGame("fortune");
                Next(0.3);
                break;
            }
            case 8:
                main.LoadGame("fortune");
                Next(1.2);
                break;
            case 9:
            {
                var chest = ChestAt(_ftMimicIndex);
                var brute = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.DisplayName == MimicRules.EliteName);
                GD.Print($"[fortune] after save/load: chest {(chest != null ? $"sprung={chest.Sprung} opened={chest.Opened}" : "missing")}, brute {(brute != null ? $"present {brute.GlobalPosition.DistanceTo(chest?.GlobalPosition ?? Vector3.Zero):0.0} m from the chest, unaware={brute.IsUnaware}" : "missing")}");
                if (chest == null) { Fail("the mimic chest is gone after the load"); _ftStep = 10; _ftAt = _elapsed; return; }
                if (!chest.Sprung || chest.Opened) Fail("the loaded chest is not sprung-and-unopened (the roll was lost or given free)");
                if (brute == null) { Fail("the brute did not come back on load"); Next(); return; }
                if (!brute.IsAlive) Fail("the restored brute is dead already");
                if (!brute.IsUnaware) Fail("the restored brute is not idle");
                if (brute.GlobalPosition.DistanceTo(chest.GlobalPosition) > 2.5f) Fail("the restored brute does not stand at the chest");
                party.Teleport(Beside(chest), chest.GlobalPosition);
                brute.Kill();
                Next(2.0);
                break;
            }
            case 10:
            {
                if (ChestAt(_ftMimicIndex) is { } chest && _ftMimicFloor == floor)
                {
                    var drops = root.GetChildren().OfType<LootDrop>().Count(d => d.GlobalPosition.DistanceTo(chest.GlobalPosition) <= 3f);
                    GD.Print($"[fortune] the brute dead: chest opened={chest.Opened}, {drops} drop(s) within 3 m");
                    if (!chest.Opened) Fail("the chest did not yield when the brute died");
                    if (drops == 0) Fail("no loot lies by the chest");
                }
                MimicRules.ForcedSpawnIndex = null;
                // 7. the bound chest and the hunt: this floor's, or the first floor in 5..9 that has one
                if (layout.BoundChest >= 0) _ftBoundFloor = floor;
                else for (var f = 5; f <= 9 && _ftBoundFloor < 0; f++) if (Preview(f).BoundChest >= 0) _ftBoundFloor = f;
                if (_ftBoundFloor < 0) { Fail("no floor in 5..9 has a bound chest"); _ftStep = 12; _ftAt = _elapsed; return; }
                if (_ftBoundFloor == floor) Next(); else Jump(_ftBoundFloor);
                break;
            }
            case 11 when floor == _ftBoundFloor:
            {
                Quiet();
                var bound = ChestAt(layout.BoundChest);
                if (bound == null) { Fail("the bound chest node is missing"); _ftStep = 13; _ftAt = _elapsed; return; }
                GD.Print($"[fortune] bound chest {layout.BoundChest}: bound={bound.Bound} prompt \"{bound.Prompt}\" table {bound.TableId}");
                if (!bound.Bound) Fail("the layout's bound chest is not Bound");
                if (bound.Prompt != FortuneRules.BoundPrompt) Fail($"the bound chest's prompt is \"{bound.Prompt}\"");
                if (bound.TableId != ChestRules.VaultTable) Fail($"the bound chest rolls {bound.TableId}, want table.vault");
                party.Teleport(Beside(bound), bound.GlobalPosition);
                bound.Interact(party, party.Lead);
                var vulnerable = party.Members.Where(m => m.IsAlive).All(m => m.Effects.Has(StatusKind.Vulnerable));
                GD.Print($"[fortune] opened: every living member Vulnerable={vulnerable}");
                if (!vulnerable) Fail("the party is not Vulnerable after opening the bound chest");
                Next(1.0);
                break;
            }
            case 12:
            {
                var hunter = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.DisplayName == FortuneRules.HunterName);
                var state = FloorStateCapture.Capture(root, layout, b.Contents, Game.Instance.ExplorationFor(floor));
                GD.Print($"[fortune] a second later: hunter {(hunter != null ? $"{hunter.Profile.Id} unaware={hunter.IsUnaware} {hunter.GlobalPosition.DistanceTo(party.Lead.GlobalPosition):0.0} m off" : "missing")}, BoundOpened saved={state.BoundOpened}");
                if (hunter == null) Fail("no hunter came");
                else if (hunter.IsUnaware) Fail("the hunter is not hunting");
                if (!state.BoundOpened) Fail("BoundOpened is not captured");
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                // 8. a secret room, found once
                for (var f = 3; f <= 8 && _ftSecretFloor < 0; f++)
                {
                    var l = f == floor ? layout : Preview(f);
                    if (l.SecretRooms.Count > 0) { _ftSecretFloor = f; _ftSecretIndex = l.SecretRooms[0].SecretIndex; }
                }
                if (_ftSecretFloor < 0) { GD.Print("[fortune] no secret room in 3..8"); _ftStep = 17; _ftAt = _elapsed; return; }
                GD.Print($"[fortune] secret room {_ftSecretIndex} on floor {_ftSecretFloor}");
                if (_ftSecretFloor == floor) Next(); else Jump(_ftSecretFloor);
                break;
            }
            case 13 when floor == _ftSecretFloor:
            {
                Quiet();
                var r = layout.SecretRooms.FirstOrDefault(x => x.SecretIndex == _ftSecretIndex);
                if (r == null) { Fail("the previewed secret room is not in the built layout"); Next(); return; }
                var inside = r.Room.Cells().First(c => c != r.Chest && c != r.Plaque && c.ManhattanDistance(r.Chest) == 1);
                var plaque = root.GetChildren().OfType<Plaque>().FirstOrDefault(p => DungeonBuilder.WorldToCell(p.GlobalPosition) == r.Plaque);
                GD.Print($"[fortune] room {r.Room} chest {r.Chest} plaque {r.Plaque} ({(plaque != null ? $"\"{plaque.Text}\"" : "missing")}); stepping onto {inside}");
                if (plaque == null || string.IsNullOrWhiteSpace(plaque.Text)) Fail("no plaque with text in the secret room");
                _ftSecretFound = 0;
                party.Teleport(Stand(inside), b.Surface(r.Chest));
                Next(0.6);
                break;
            }
            case 14 when _ftSecretFloor >= 0 && floor == _ftSecretFloor:
            {
                var record = Game.Instance.Adventure.Memory.For(floor);
                GD.Print($"[fortune] inside: SecretFound fired {_ftSecretFound}, record {record.SecretsFound}/{record.SecretsTotal} (layout has {layout.Secrets.Count})");
                if (_ftSecretFound != 1) Fail($"SecretFound fired {_ftSecretFound} times on entering, want 1");
                if (record.SecretsFound != 1) Fail($"the floor record counts {record.SecretsFound} secrets found, want 1");
                if (record.SecretsTotal != layout.Secrets.Count) Fail($"the floor record's total is {record.SecretsTotal}, want {layout.Secrets.Count}");
                party.Teleport(Stand(layout.UpStairs));
                Next(0.6);
                break;
            }
            case 15 when _ftSecretFloor >= 0:
            {
                var r = layout.SecretRooms.First(x => x.SecretIndex == _ftSecretIndex);
                party.Teleport(Stand(r.Room.Cells().First(c => c != r.Chest && c.ManhattanDistance(r.Chest) == 1)), b.Surface(r.Chest));
                Next(0.6);
                break;
            }
            case 16 when _ftSecretFloor >= 0:
                GD.Print($"[fortune] out and in again: SecretFound fired {_ftSecretFound}");
                if (_ftSecretFound != 1) Fail($"SecretFound fired {_ftSecretFound} times after leaving and returning, want 1");
                Next();
                break;
            case 17:
            {
                // 9. the lock: three fumbles jam it, the noise wakes a sleeper, a key still opens it
                Quiet();
                var chest = root.GetChildren().OfType<Chest>().FirstOrDefault(c => !c.Opened && !c.IsMimic && !c.Bound && c.Locked)
                    ?? root.GetChildren().OfType<Chest>().FirstOrDefault(c => !c.Opened && !c.IsMimic && !c.Bound);
                if (chest == null) { Fail("no chest to lock on this floor"); _ftStep = 23; _ftAt = _elapsed; return; }
                chest.Locked = true;
                _ftLockIndex = chest.SpawnIndex;
                var keys = party.Bag.Count("item.iron_key");
                if (keys > 0) party.Bag.Consume("item.iron_key", keys);
                party.Teleport(Beside(chest), chest.GlobalPosition);
                var id = b.RegularEnemies(floor)[0];
                var deaf = b.EnemyProfile(id)! with { DetectRange = 0f }; // it notices nothing by sight: only the noise can wake it
                _ftSleeper = b.SpawnEnemy(id, chest.GlobalPosition + new Vector3(0, 0.1f, 6f), floor, profile: deaf);
                LockRules.ForcedChance = 0f;
                GD.Print($"[fortune] lock test on chest {_ftLockIndex} (locked={chest.Locked}, {keys} key(s) dropped), sleeper {id} 6 m off");
                Next(0.3);
                break;
            }
            case 18:
            {
                var chest = ChestAt(_ftLockIndex)!;
                var awakeBefore = _ftSleeper != null && IsInstanceValid(_ftSleeper) && !_ftSleeper.IsUnaware;
                chest.Interact(party, party.Lead);
                var awake = _ftSleeper != null && IsInstanceValid(_ftSleeper) && !_ftSleeper.IsUnaware;
                GD.Print($"[fortune] fumble 1: fails={chest.LockFails} jammed={chest.Jammed} sleeper awake {awakeBefore} -> {awake}");
                if (awakeBefore) Fail("the sleeper was awake before the fumble");
                if (!awake) Fail("the fumble's noise did not wake the sleeper 6 m off");
                _ftPicks = 1;
                Next(1.3);
                break;
            }
            case 19:
            {
                var chest = ChestAt(_ftLockIndex)!;
                chest.Interact(party, party.Lead);
                _ftPicks++;
                GD.Print($"[fortune] fumble {_ftPicks}: fails={chest.LockFails} jammed={chest.Jammed} prompt \"{chest.Prompt}\"");
                if (_ftPicks < 3) { _ftAt = _elapsed + 1.3; return; }
                if (!chest.Jammed) Fail("three fumbles did not jam the lock");
                if (!chest.Prompt.Contains("Jammed")) Fail($"the jammed prompt reads \"{chest.Prompt}\"");
                main.SaveGame("fortune");
                Next(0.3);
                break;
            }
            case 20:
                main.LoadGame("fortune");
                Next(1.2);
                break;
            case 21:
            {
                var chest = ChestAt(_ftLockIndex);
                GD.Print($"[fortune] after save/load: chest {(chest != null ? $"locked={chest.Locked} jammed={chest.Jammed} opened={chest.Opened}" : "missing")}");
                if (chest == null) { Fail("the jammed chest is gone after the load"); _ftStep = 23; _ftAt = _elapsed; return; }
                if (!chest.Jammed) Fail("the jam did not survive the save");
                party.Teleport(Beside(chest), chest.GlobalPosition);
                party.Bag.TryAdd(cat.Create("item.iron_key"));
                chest.Interact(party, party.Lead);
                GD.Print($"[fortune] with a key: opened={chest.Opened} locked={chest.Locked}");
                if (!chest.Opened) Fail("a key did not open the jammed chest");
                LockRules.ForcedChance = null;
                main.SaveGame("fortune");
                Next(0.3);
                break;
            }
            case 22:
                main.LoadGame("fortune");
                Next(1.2);
                break;
            case 23:
            {
                if (_ftLockIndex >= 0)
                {
                    var chest = ChestAt(_ftLockIndex);
                    var state = FloorStateCapture.Capture(root, layout, b.Contents, Game.Instance.ExplorationFor(floor));
                    GD.Print($"[fortune] loaded again: chest opened={chest?.Opened} jammed list [{string.Join(",", state.JammedChests)}]");
                    if (chest is not { Opened: true }) Fail("the opened chest is not open after the load");
                    if (state.JammedChests.Contains(_ftLockIndex)) Fail("the opened chest is still listed as jammed");
                }
                // 10. the Lucky Dip
                Quiet();
                var merchant = root.GetNodeOrNull<Npc>("Npc_merchant");
                if (merchant == null || layout.SafeRoom < 0) { GD.Print("[fortune] no merchant on this floor: the Lucky Dip is skipped"); Fail("no merchant to run the Lucky Dip at"); _ftStep = 24; _ftAt = _elapsed; return; }
                party.Teleport(Stand(SafeRoomRules.MerchantCell(layout)), merchant.GlobalPosition);
                party.SetGold(0);
                party.AddGold(10000);
                var stock = party.ShopStockFor(floor);
                var gambledBefore = _ftGambled;
                var first = party.LuckyDipBuy(0);
                var held = party.Bag.Count(stock.Dip[0].DefId) > 0 || Enumerable.Range(0, party.Bag.Capacity).Any(i => party.Bag[i]?.DisplayName == stock.Dip[0].DisplayName);
                GD.Print($"[fortune] dip 0: refusal={first ?? "none"} item {stock.Dip[0]} ({stock.Dip[0].Kind}, {stock.Dip[0].Rarity}) in bag={held} gold={party.Gold} Gambled fired {_ftGambled - gambledBefore} bought [{string.Join(",", party.LuckyDipBoughtOnFloor)}]");
                if (first != null) Fail($"the first bundle was refused: {first}");
                if (stock.Dip[0].Kind != SlotKind.Weapon) Fail("bundle 0 is not a weapon");
                if (!held) Fail("the bundle's item is not in the bag");
                if (_ftGambled - gambledBefore != 1) Fail("Gambled did not fire once");
                if (!party.LuckyDipBoughtOnFloor.Contains(0)) Fail("slot 0 is not marked bought");
                var again = party.LuckyDipBuy(0);
                if (again != "Already bought.") Fail($"buying slot 0 again said \"{again}\"");
                party.SetGold(0);
                var brokeBefore = _ftBroke;
                var broke = party.LuckyDipBuy(1);
                GD.Print($"[fortune] broke: \"{broke}\" BrokeAtShop fired {_ftBroke - brokeBefore}");
                if (broke != "Not enough gold.") Fail($"the broke party was told \"{broke}\"");
                if (_ftBroke - brokeBefore != 1) Fail("BrokeAtShop did not fire");
                party.AddGold(10000);
                main.Hud.Shop.Open(floor, merchant);
                main.Hud.Shop.BuyDip(2);
                var tab = party.LuckyDipBoughtOnFloor.Contains(2);
                main.Hud.Shop.Close();
                GD.Print($"[fortune] the tab's path bought slot 2={tab}; bought [{string.Join(",", party.LuckyDipBoughtOnFloor)}]");
                if (!tab) Fail("the shop tab's BuyDip did not buy slot 2");
                Next();
                break;
            }
            case 24:
            {
                // 11. a save round trip keeps it all
                _ftBoonsBefore = adv.Boons.ToList(); _ftCursesBefore = adv.Curses.ToList();
                _ftDipBefore = party.LuckyDipBoughtOnFloor.OrderBy(i => i).ToList();
                _ftShrineUsedBefore = ShrineNode()?.Used ?? false;
                _ftArmorBeforeSave = party.Members[0].Stats[Stat.Armor];
                if (!adv.Boons.Contains("ferryman")) FortuneDirector.Instance!.TakeBoon("ferryman", 0);
                FortuneDirector.Instance!.OnFloorStart();
                GD.Print($"[fortune] before the save: death wards {party.Members[0].DeathWards}");
                _ftBoonsBefore = adv.Boons.ToList();
                main.SaveGame("fortune");
                Next(0.3);
                break;
            }
            case 25:
                main.LoadGame("fortune");
                Next(1.2);
                break;
            case 26:
            {
                var adv2 = Game.Instance.Adventure;
                if (party.Members[0].DeathWards != 1) Fail($"Pennies for the Ferryman's ward is {party.Members[0].DeathWards} after the load, want 1");
                var shrine = ShrineNode();
                GD.Print($"[fortune] round trip: boons [{string.Join(",", adv2.Boons)}] curses [{string.Join(",", adv2.Curses)}] shrine used={shrine?.Used} (was {_ftShrineUsedBefore}) dip [{string.Join(",", party.LuckyDipBoughtOnFloor.OrderBy(i => i))}] armour {party.Members[0].Stats[Stat.Armor]:0.##} (was {_ftArmorBeforeSave:0.##}, base {_ftArmorBase:0.##})");
                if (!adv2.Boons.SequenceEqual(_ftBoonsBefore)) Fail("the boons changed over the save");
                if (!adv2.Curses.SequenceEqual(_ftCursesBefore)) Fail("the curses changed over the save");
                if (shrine != null && shrine.Used != _ftShrineUsedBefore) Fail("the shrine's used state changed over the save");
                if (!party.LuckyDipBoughtOnFloor.OrderBy(i => i).SequenceEqual(_ftDipBefore)) Fail("the bought bundles changed over the save");
                if (Math.Abs(party.Members[0].Stats[Stat.Armor] - _ftArmorBeforeSave) > 0.01f) Fail("the boon's armour did not survive the load (the director did not refresh)");
                if (party.Members[0].Stats[Stat.Armor] < _ftArmorBase + 12f - 0.01f) Fail("Thick Hide's +12 is gone after the load");
                // 12. a mine's drift ambusher stays dead once its corpse is gone
                for (var f = 3; f <= 12 && _ftMineFloor < 0; f++) if (Preview(f) is { Kind: LayoutKind.Mine } l && l.Caches.Any(c => c.Kind == CacheKind.Ambusher)) _ftMineFloor = f;
                if (_ftMineFloor < 0) { GD.Print("[fortune] no mine with an ambusher cache in 3..12"); Finish(); return; }
                Jump(_ftMineFloor);
                break;
            }
            case 27 when floor == _ftMineFloor:
            {
                var ambusher = root.GetChildren().OfType<Enemy>().FirstOrDefault(e => e.SpawnIndex >= DungeonBuilder.CacheAmbusherBase);
                GD.Print($"[fortune] mine floor {floor}: {layout.Drifts.Count} drifts, caches [{string.Join(",", layout.Caches.Select(c => c.Kind))}], ambusher {(ambusher != null ? $"{ambusher.Profile.Id} #{ambusher.SpawnIndex} unaware={ambusher.IsUnaware}" : "missing")}");
                if (ambusher == null) { Fail("no ambusher stands at a drift's end"); Finish(); return; }
                _ftAmbusherIndex = ambusher.SpawnIndex;
                ambusher.Kill();
                Next(6.0); // past the corpse timer: the node is freed
                break;
            }
            case 28:
            {
                var still = root.GetChildren().OfType<Enemy>().Any(e => e.SpawnIndex == _ftAmbusherIndex);
                var state = FloorStateCapture.Capture(root, layout, b.Contents, Game.Instance.ExplorationFor(floor));
                GD.Print($"[fortune] six seconds later: corpse present={still}, dead list has #{_ftAmbusherIndex}={state.DeadEnemies.Contains(_ftAmbusherIndex)}");
                if (!state.DeadEnemies.Contains(_ftAmbusherIndex)) Fail("a killed drift ambusher is not saved as dead once its corpse is freed (it would come back)");
                Finish();
                break;
            }
        }
    }
}
