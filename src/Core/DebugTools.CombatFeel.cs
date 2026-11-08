using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Enemies.Ai;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// <c>--test-combat-feel</c>: the Depths' combat in one quiet room (seed 7, lethality 1): a perfect block then an ordinary block, a parry with its
/// riposte, the parry's economy, a dodge (and that gas still lands through one), the stagger a heavy blow earns, damage numbers, the hit marker
/// and the bars (none for a horde), the "Damage numbers" setting, Shatter / Ignite / Bleed / Vulnerable / a frosted wind-up, a boss's telegraph and
/// the companions' dodge, the boss bar, and the vignette, shake, low-HP cue, banner, toast and legendary beam. Prints [combat-feel] lines and RESULT.
/// </summary>
public partial class DebugTools
{
    private bool _testCombatFeel;
    private int _cfStep;
    private double _cfNext, _cfStepStarted, _cfSwingAt;
    private int _cfTries;
    private Enemy? _cfE1, _cfE2, _cfBoss, _cfHorde;
    private Vector3 _cfStand, _cfForward, _cfSableAt;
    private float _cfHp, _cfStamina, _cfEnemyHp;
    private readonly List<string> _cfFails = new();
    private readonly List<string> _cfLog = new();
    private int _cfPerfectBlocks, _cfParried, _cfPerfectBlows, _cfLowHp, _cfHits;
    private bool _cfListening;
    private int _cfDodgesBefore;

    private void CfPerfectBlock(int i) => _cfPerfectBlocks++;
    private void CfParried(int i) => _cfParried++;
    private void CfPerfectBlow(int i) => _cfPerfectBlows++;
    private void CfLowHp(int i) => _cfLowHp++;
    private void CfMessage(string text) => _cfLog.Add(text);
    private void CfHurt(Enemy e, float amount, HitFlavor flavor, int by) { if (e == _cfE1 || e == _cfBoss) _cfHits++; }
    private const double Tick = 0.05; // a _Process frame can run with no physics tick between: "one tick later" waits this long

    private void RunCombatFeelTest()
    {
        if (_cfStep >= 99) return;
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        if (_elapsed > 70) { Fail($"timed out at step {_cfStep}"); Finish(); return; }
        if (_elapsed < _cfNext) return;
        var party = main.Party;
        var b = main.Builder;
        var hud = main.Hud;
        var brand = Math.Max(0, Array.FindIndex(party.Members, m => m.Name == "Brand"));
        var sable = Math.Max(0, Array.FindIndex(party.Members, m => m.Class.Id == "class.rogue"));
        var since = _elapsed - _cfStepStarted;

        void Fail(string why) { _cfFails.Add(why); GD.Print($"[combat-feel] FAIL (step {_cfStep}): {why}"); }
        void Finish()
        {
            if (_cfListening)
            {
                var bus = EventBus.Instance;
                bus.PerfectBlock -= CfPerfectBlock; bus.Parried -= CfParried; bus.PerfectBlow -= CfPerfectBlow; bus.LowHp -= CfLowHp; bus.Message -= CfMessage; bus.EnemyHurt -= CfHurt;
                _cfListening = false;
            }
            GD.Print(_cfFails.Count == 0 ? "[combat-feel] RESULT ok" : $"[combat-feel] RESULT FAIL ({string.Join("; ", _cfFails)})");
            _cfStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        void Go(int step, double wait = 0) { _cfStep = step; _cfStepStarted = _elapsed; _cfNext = _elapsed + wait; _cfLog.Clear(); }
        void Wait(double seconds) => _cfNext = _elapsed + seconds; // stay on this step
        bool Said(string fragment) => _cfLog.Any(l => l.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        void Check(bool ok, string what) { if (!ok) Fail(what); }
        void Stun(Enemy? e, float seconds = 30f) { if (e != null && IsInstanceValid(e) && e.IsAlive) e.Effects.Apply(new StatusApplication(StatusKind.Stun, seconds, 0)); }
        void Unstun(Enemy? e) { if (e != null && IsInstanceValid(e)) e.Effects.Remove(StatusKind.Stun); }
        bool WindingUp(Enemy? e, float leftAtMost) => e != null && IsInstanceValid(e) && e.IsAlive && e.State is AttackState && e.WindupLeft > 0f && e.WindupLeft <= leftAtMost && !e.Staggered;
        Adventurer Adv(int i) => party.Adventurers[i];
        void Stand(int hero, Enemy e, float dist = 1.5f)
        {
            var at = e.GlobalPosition - _cfForward * dist;
            Adv(hero).Teleport(at with { Y = e.GlobalPosition.Y + 0.2f }, e.GlobalPosition + Vector3.Up * 1.0f);
        }
        Enemy? SpawnMelee(Vector3 at)
        {
            var regulars = b.RegularEnemies(b.CurrentFloor);
            var id = regulars.FirstOrDefault(x => b.EnemyProfile(x)?.Kind == EnemyKind.Melee && b.EnemyProfile(x)?.IsBoss != true) ?? regulars[0];
            var e = b.SpawnEnemy(id, at, b.CurrentFloor);
            e?.ForceAggro();
            return e;
        }
        void Sure(int hero) { var m = party.Members[hero]; if (!m.SkillModifiers.Any(x => x.Source == "test")) { m.SkillModifiers.Add(new StatModifier(Stat.Accuracy, ModOp.Add, 1000f, "test")); m.Recalculate(); } }
        void Free(ref Enemy? e) { if (e != null && IsInstanceValid(e)) e.QueueFree(); e = null; }
        int DodgesOfCompanions() => party.Adventurers.Where(a => IsInstanceValid(a) && a.Control == ControlKind.Companion).Sum(a => a.DodgesMade);
        bool DecalPresent() => root.GetChildren().OfType<Combat.Fx.TelegraphDecal>().Any(d => !d.IsQueuedForDeletion());
        void Shield(Enemy? e) { if (e != null && IsInstanceValid(e) && e.IsAlive) e.Effects.Apply(new StatusApplication(StatusKind.Shield, 120f, StatusEffectController.MaxShield)); } // the blow lands, the body survives it
        bool Gone(Enemy? e, string who) { if (e != null && IsInstanceValid(e) && e.IsAlive) return false; Fail($"{who} died or vanished at step {_cfStep}"); Finish(); return true; }

        switch (_cfStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                var bus = EventBus.Instance;
                bus.PerfectBlock += CfPerfectBlock; bus.Parried += CfParried; bus.PerfectBlow += CfPerfectBlow; bus.LowHp += CfLowHp; bus.Message += CfMessage; bus.EnemyHurt += CfHurt;
                _cfListening = true;
                AbilitiesTakeControl(brand);
                Sure(brand);
                var room = layout.Rooms.OrderByDescending(r => r.W * r.H).First();
                _cfStand = b.Surface(room.Center) + Vector3.Up * 0.3f;
                _cfForward = new Vector3(0, 0, -1);
                party.Teleport(_cfStand, _cfStand + _cfForward * 1.5f);
                // data: every resist in the roster is within the clamp
                var offenders = b.Roster.Values.Where(p => p.ResistFire > CombatRules.MaxResist || p.ResistFrost > CombatRules.MaxResist || p.ResistPoison > CombatRules.MaxResist || p.ResistArcane > CombatRules.MaxResist).Select(p => p.Id).ToList();
                Check(offenders.Count == 0, $"resists above {CombatRules.MaxResist} in data: {string.Join(", ", offenders)}");
                GD.Print($"[combat-feel] data: {b.Roster.Count} profiles, resists <= {CombatRules.MaxResist}: {(offenders.Count == 0 ? "ok" : "FAIL")}");
                _cfE1 = SpawnMelee(_cfStand - Vector3.Up * 0.2f + _cfForward * 1.5f);
                if (_cfE1 == null) { Fail("could not spawn an enemy"); Finish(); return; }
                GD.Print($"[combat-feel] floor {b.CurrentFloor} seed {Game.Instance.GameSeed}: E1 {_cfE1.Profile.Id} windup {_cfE1.WindupSeconds:0.00} s recover {_cfE1.RecoverSeconds:0.00} s range {_cfE1.AttackRange:0.0}; Brand hp {party.Members[brand].Hp:0} stamina {party.Members[brand].Stamina:0} at {Adv(brand).GlobalPosition:0.0}");
                Stun(_cfE1, 1.0f); // let both bodies settle on the floor before the first swing
                Go(1, 1.2);
                break;
            }
            case 1: // 1. perfect block: raise the guard in the last 0.15 s of the wind-up
            {
                if (Gone(_cfE1, "E1")) return;
                var e = _cfE1!;
                if (since > 8) { Fail($"E1 never wound up (state {e.State.Name}, dist {e.DistanceToParty():0.0})"); Finish(); return; }
                if (e.State is AttackState && e.WindupLeft > 0f && !_cfTries.Equals(-1))
                {
                    Check(e.CurrentAnim == "Punch", $"the wind-up clip is {e.CurrentAnim}, not Punch");
                    Check(e.WindupLeft <= e.WindupSeconds + 0.01f, $"WindupLeft {e.WindupLeft:0.00} above WindupSeconds {e.WindupSeconds:0.00}");
                    _cfTries = -1; // seen once
                }
                if (!WindingUp(e, 0.15f)) break;
                var m = party.Members[brand];
                _cfHp = m.Hp; _cfStamina = m.Stamina; _cfPerfectBlocks = 0;
                Adv(brand).DebugIntent = new InputIntent { Guard = true, Live = true };
                GD.Print($"[combat-feel] 1 guard raised with {e.WindupLeft:0.00} s of wind-up left; Brand at {Adv(brand).GlobalPosition:0.0}, E1 at {e.GlobalPosition:0.0}");
                Go(2, e.WindupSeconds + 0.25);
                break;
            }
            case 2:
            {
                var m = party.Members[brand];
                var e = _cfE1!;
                GD.Print($"[combat-feel] 2 perfect block: hp {_cfHp:0}->{m.Hp:0} stamina {_cfStamina:0}->{m.Stamina:0} events {_cfPerfectBlocks} staggered {e.Staggered} log [{string.Join(" | ", _cfLog)}]");
                Check(m.Hp >= _cfHp - 0.01f, "a perfect block cost hp"); // regen may have added a little
                Check(Math.Abs(m.Stamina - _cfStamina) < 0.01f, "a perfect block cost stamina");
                Check(_cfPerfectBlocks == 1, $"PerfectBlock events {_cfPerfectBlocks}");
                Check(e.Staggered, "the attacker was not staggered by the perfect block");
                Check(Said("perfectly blocks"), "no 'perfectly blocks' line");
                _cfTries = 0;
                Go(3, 0.2); // the guard stays up
                break;
            }
            case 3: // 2. the guard held: the next swing is an ordinary block
            {
                var e = _cfE1!;
                var m = party.Members[brand];
                if (since > 8) { Fail($"E1 never swung again (state {e.State.Name})"); Finish(); return; }
                if (_cfSwingAt == 0)
                {
                    if (!WindingUp(e, 0.3f)) break;
                    _cfHp = m.Hp; _cfStamina = m.Stamina; _cfLog.Clear();
                    _cfSwingAt = _elapsed;
                    Wait(e.WindupLeft + 0.25);
                    break;
                }
                _cfSwingAt = 0;
                if (Said("misses Brand") && _cfTries++ < 3) { GD.Print("[combat-feel] 3 the swing missed; waiting for the next"); break; }
                var maxDmg = e.Attack.MaxDamage * 0.25f * 1.5f + 1f;
                GD.Print($"[combat-feel] 3 ordinary block: hp {_cfHp:0}->{m.Hp:0} (max loss {maxDmg:0.0}) stamina {_cfStamina:0}->{m.Stamina:0} events {_cfPerfectBlocks} log [{string.Join(" | ", _cfLog)}]");
                Check(Math.Abs((_cfStamina - m.Stamina) - DefenseRules.BlockStamina) <= 0.5f, $"block stamina {_cfStamina - m.Stamina:0.0}, expected {DefenseRules.BlockStamina}");
                Check(_cfHp - m.Hp > 0f && _cfHp - m.Hp <= maxDmg, $"blocked hit took {_cfHp - m.Hp:0.0}");
                Check(_cfPerfectBlocks == 1, "a held guard counted as a perfect block");
                Check(Said("(blocked)"), "no '(blocked)' line");
                Adv(brand).DebugIntent = null;
                Stun(e);
                Go(4, 0.2);
                break;
            }
            case 4: // 3. the parry: Sable learns the Duelist branch to its Parry node
            {
                AbilitiesTakeControl(sable);
                var s = party.Members[sable];
                s.SkillPoints += 4;
                foreach (var id in new[] { "skill.rogue.duelist.footwork", "skill.rogue.duelist.flurry", "skill.rogue.duelist.venom_mastery", "skill.rogue.duelist.parry" })
                {
                    var skill = Game.Instance.Skills.Get(id);
                    if (skill == null || !SkillRules.Learn(s, skill, Game.Instance.Skills)) Fail($"could not learn {id}: {(skill == null ? "unknown" : SkillRules.WhyCannotLearn(s, skill))}");
                }
                AbilitiesRefill(s);
                Check(party.Trigger(s, TriggerKind.Riposte) > 0f, "the Parry node grants no Riposte");
                Stand(sable, _cfE1!);
                Unstun(_cfE1);
                _cfTries = 0; _cfSwingAt = 0;
                Go(5, 0.3);
                break;
            }
            case 5: // press the parry in the last 0.2 s of the wind-up
            {
                var e = _cfE1!;
                var s = party.Members[sable];
                if (since > 8) { Fail($"E1 never wound up at Sable (state {e.State.Name}, dist {e.DistanceToParty():0.0})"); Finish(); return; }
                if (!WindingUp(e, 0.2f)) break;
                _cfHp = s.Hp; _cfStamina = s.Stamina; _cfEnemyHp = e.Hp; _cfParried = 0; _cfLog.Clear();
                Adv(sable).DebugIntent = new InputIntent { ParryPressed = true, Live = true };
                _cfSwingAt = e.WindupLeft;
                Go(6, Tick);
                break;
            }
            case 6: // one tick later: the press is spent
            {
                Adv(sable).DebugIntent = null;
                var s = party.Members[sable];
                Check(Adv(sable).Parrying, "the parry window did not open");
                Check(Math.Abs((_cfStamina - s.Stamina) - DefenseRules.ParryStamina) <= 0.5f, $"parry stamina {_cfStamina - s.Stamina:0.0}, expected {DefenseRules.ParryStamina}");
                Go(7, Math.Max(0.05, _cfSwingAt + 0.25 - Tick));
                break;
            }
            case 7:
            {
                var e = _cfE1!;
                var s = party.Members[sable];
                GD.Print($"[combat-feel] 5 parry: hp {_cfHp:0}->{s.Hp:0} events {_cfParried} staggered {e.Staggered} E1 hp {_cfEnemyHp:0}->{e.Hp:0} log [{string.Join(" | ", _cfLog)}]");
                Check(s.Hp >= _cfHp - 0.01f, "a parried blow cost hp");
                Check(_cfParried == 1, $"Parried events {_cfParried}");
                Check(e.Staggered, "the attacker was not staggered by the parry");
                Check(e.Hp < _cfEnemyHp && Said("ripostes"), "no riposte through the Parry node");
                Stun(e);
                Go(35, DefenseRules.ParryCooldown + 0.1); // the real parry's cooldown runs out first
                break;
            }
            case 35: // 6. the parry's economy
            {
                var s = party.Members[sable];
                AbilitiesRefill(s);
                _cfStamina = s.Stamina; _cfParried = 0;
                Adv(sable).DebugIntent = new InputIntent { ParryPressed = true, Live = true };
                Go(8, Tick);
                break;
            }
            case 8:
            {
                Adv(sable).DebugIntent = null;
                var s = party.Members[sable];
                var first = _cfStamina - s.Stamina;
                GD.Print($"[combat-feel] 6 parry with nothing coming: stamina -{first:0.0}, events {_cfParried}");
                Check(Math.Abs(first - DefenseRules.ParryStamina) <= 0.5f && _cfParried == 0, "a missed parry should cost its stamina and nothing else");
                _cfStamina = s.Stamina;
                Go(9, 0.2);
                break;
            }
            case 9:
            {
                Adv(sable).DebugIntent = new InputIntent { ParryPressed = true, Live = true };
                Go(10, Tick);
                break;
            }
            case 10:
            {
                Adv(sable).DebugIntent = null;
                var s = party.Members[sable];
                GD.Print($"[combat-feel] 6 parry again inside the cooldown: stamina {_cfStamina:0.0}->{s.Stamina:0.0}");
                Check(s.Stamina >= _cfStamina - 0.01f, "a parry inside the cooldown cost stamina");
                // 7. the dodge
                AbilitiesRefill(s);
                Stand(sable, _cfE1!);
                Unstun(_cfE1);
                _cfTries = 0;
                Go(11, 0.3);
                break;
            }
            case 11:
            {
                var e = _cfE1!;
                var s = party.Members[sable];
                if (since > 8) { Fail($"E1 never wound up for the dodge (state {e.State.Name})"); Finish(); return; }
                if (!WindingUp(e, 0.2f)) break;
                _cfHp = s.Hp; _cfStamina = s.Stamina; _cfSableAt = Adv(sable).GlobalPosition; _cfLog.Clear();
                Adv(sable).DebugIntent = new InputIntent { DodgePressed = true, Move = new Vector2(0, 1), Live = true };
                _cfSwingAt = e.WindupLeft;
                Go(12, Tick);
                break;
            }
            case 12: // one tick in: the window is open; a creature's blow lands on nothing
            {
                var e = _cfE1!;
                var s = party.Members[sable];
                Adv(sable).DebugIntent = null;
                Check(Adv(sable).Dodging, "the dodge did not start");
                Check(Adv(sable).DodgesMade == 1, $"DodgesMade {Adv(sable).DodgesMade}");
                Check(Math.Abs((_cfStamina - s.Stamina) - DefenseRules.DodgeStamina) <= 0.5f, $"dodge stamina {_cfStamina - s.Stamina:0.0}, expected {DefenseRules.DodgeStamina}");
                Adv(sable).ReceiveAttack(e.Stats, e.Attack, e.DisplayName, e); // the blow a creature lands during the burst
                Check(Said("dodges"), "no 'dodges' line for a creature's blow during the burst");
                Go(13, 0.1);
                break;
            }
            case 13:
            {
                Adv(sable).DebugIntent = new InputIntent { DodgePressed = true, Live = true }; // 0.1 s after the first: refused
                Go(14, Math.Max(0.05, _cfSwingAt + 0.25 - 0.1 - Tick));
                break;
            }
            case 14:
            {
                var s = party.Members[sable];
                Adv(sable).DebugIntent = null;
                var moved = Adv(sable).GlobalPosition.DistanceTo(_cfSableAt);
                GD.Print($"[combat-feel] 7 dodge: hp {_cfHp:0}->{s.Hp:0} moved {moved:0.00} m dodges {Adv(sable).DodgesMade} log [{string.Join(" | ", _cfLog)}]");
                Check(s.Hp >= _cfHp - 0.01f, "the dodging body was hurt");
                Check(moved >= 1.5f, $"the burst moved {moved:0.00} m");
                Check(Adv(sable).DodgesMade == 1, "a second dodge inside the cooldown was not refused");
                Stun(_cfE1);
                // 8. a dodge never saves you from the dungeon
                AbilitiesRefill(s);
                Go(15, DefenseRules.DodgeCooldown + 0.1);
                break;
            }
            case 15:
            {
                var s = party.Members[sable];
                s.Effects.Apply(new StatusApplication(StatusKind.Stun, 0.5f, 0)); // a stunned defender is always hit: the roll is not what is tested here
                Adv(sable).DebugIntent = new InputIntent { DodgePressed = true, Live = true };
                Go(16, Tick);
                break;
            }
            case 16:
            {
                var s = party.Members[sable];
                Adv(sable).DebugIntent = null;
                Check(Adv(sable).Dodging, "the hazard dodge did not start");
                var before = s.Hp;
                var expected = DamagePipeline.Mitigate(5f, DamageType.Physical, s.Stats, s.Effects);
                Adv(sable).ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = 5, MaxDamage = 5 }, "The gas");
                GD.Print($"[combat-feel] 8 gas through a dodge: hp {before:0.0}->{s.Hp:0.0} (expected -{expected:0.0}) log [{string.Join(" | ", _cfLog)}]");
                Check(Math.Abs((before - s.Hp) - expected) <= 0.5f, $"gas through a dodge took {before - s.Hp:0.0}, expected {expected:0.0}");
                s.Effects.Remove(StatusKind.Stun);
                // 9. the stagger a heavy blow earns
                AbilitiesTakeControl(brand);
                AbilitiesRefill(party.Members[brand]);
                Sure(brand);
                Stand(brand, _cfE1!);
                Shield(_cfE1); // Brand's heavy blow would kill a level-1 regular outright; the shield keeps the body for the stagger, number, bar and combo checks
                Unstun(_cfE1);
                _cfTries = 0;
                Go(17, 0.3);
                break;
            }
            case 17:
            {
                if (Gone(_cfE1, "E1")) return;
                var e = _cfE1!;
                if (since > 10) { Fail($"E1 never wound up for the heavy blow (state {e.State.Name})"); Finish(); return; }
                if (!(e.State is AttackState && e.WindupLeft > 0.3f && !e.Staggered)) break;
                _cfHits = 0; _cfLog.Clear();
                Shield(e);
                AbilitiesRefill(party.Members[brand]);
                party.Attack(brand, heavy: true);
                Go(18);
                break;
            }
            case 18:
            {
                if (Gone(_cfE1, "E1")) return;
                var e = _cfE1!;
                if (_cfHits == 0)
                {
                    if (_cfTries++ < 3) { GD.Print($"[combat-feel] 9 the heavy blow missed ({string.Join(" | ", _cfLog)}); again"); Go(17, 0.1); break; }
                    Fail("the heavy blow never landed"); Finish(); return;
                }
                GD.Print($"[combat-feel] 9 heavy blow: hits {_cfHits} staggered {e.Staggered} windupLeft {e.WindupLeft:0.00} numbers {hud.DamageNumbers.ActiveCount} marker {hud.HitMarker.Active} log [{string.Join(" | ", _cfLog)}]");
                Check(e.Staggered, "a heavy blow did not stagger a regular");
                Check(e.WindupLeft < 0f, "the stagger did not clear the wind-up");
                Check(hud.DamageNumbers.ActiveCount >= 1, "no damage number");
                Check(hud.HitMarker.Active, "the hit marker did not flick");
                Go(19);
                break;
            }
            case 19:
            {
                if (Gone(_cfE1, "E1")) return;
                var e = _cfE1!;
                if (since < Tick) break; // the bars place themselves on the next frame
                if (!hud.EnemyBars.Showing(e) && _cfTries++ < 1) Fail("no health bar over the hurt enemy");
                if (since > 4) { Fail("the stagger never ended"); Finish(); return; }
                if (e.Staggered) break;
                GD.Print($"[combat-feel] 9 after the stagger: state {e.State.Name} windupLeft {e.WindupLeft:0.00} (windup {e.WindupSeconds:0.00}) bar {hud.EnemyBars.Showing(e)}");
                Check(e.State is not AttackState || e.WindupLeft >= e.WindupSeconds - 0.1f, "the wind-up did not start over after the stagger");
                Stun(e);
                _cfPerfectBlows = 0; _cfTries = 0;
                Go(20, 0.1);
                break;
            }
            case 20 when _cfHits == 0 && _cfTries == 0: // a swing that missed is swung again (the hit chance caps at 95%)
            {
                Shield(_cfE1); _cfHits = 0; _cfTries = 1;
                Go(20, Tick);
                break;
            }
            case 20: // the perfect blow
            {
                if (Gone(_cfE1, "E1")) return;
                var e = _cfE1!;
                _cfHits = 0; _cfLog.Clear();
                Shield(e);
                for (var attempt = 0; attempt < 4 && _cfHits == 0; attempt++) { AbilitiesRefill(party.Members[brand]); _cfPerfectBlows = 0; party.Attack(brand, heavy: true, perfect: true); }
                GD.Print($"[combat-feel] 9 perfect blow: hits {_cfHits} events {_cfPerfectBlows} log [{string.Join(" | ", _cfLog)}]");
                Check(_cfPerfectBlows == 1, $"PerfectBlow events {_cfPerfectBlows}");
                Check(Said("perfectly smashes") || Said("critically hits") || Said("shatters"), "no perfect-blow line");
                // a horde body gets no bar
                _cfHorde = b.SpawnEnemy(e.Profile.Id, _cfStand + new Vector3(3, -0.2f, 0), b.CurrentFloor);
                if (_cfHorde != null)
                {
                    _cfHorde.SetSwarmDetail();
                    _cfHorde.ReceiveAttack(party.Members[brand].Stats, new AttackProfile { MinDamage = 1, MaxDamage = 1 }, new HitContext { DefenderStunned = true }, party.Rng, "test");
                }
                else Fail("could not spawn a horde body");
                Go(21, 0.1);
                break;
            }
            case 21:
            {
                if (_cfHorde != null) Check(!hud.EnemyBars.Showing(_cfHorde), "a horde body got a health bar");
                GD.Print($"[combat-feel] 9 horde bar: {(_cfHorde != null && hud.EnemyBars.Showing(_cfHorde) ? "shown (FAIL)" : "none")}");
                Free(ref _cfHorde);
                // 10. the setting
                Settings.DamageNumbers = false;
                Go(22, 1.0);
                break;
            }
            case 22:
            {
                if (Gone(_cfE1, "E1")) return;
                var before = hud.DamageNumbers.ActiveCount;
                Shield(_cfE1);
                AbilitiesRefill(party.Members[brand]);
                party.Attack(brand);
                GD.Print($"[combat-feel] 10 numbers off: active {before}->{hud.DamageNumbers.ActiveCount}");
                Check(hud.DamageNumbers.ActiveCount == 0, $"numbers shown with the setting off: {hud.DamageNumbers.ActiveCount}");
                Settings.DamageNumbers = true;
                // 11. statuses
                var e = _cfE1!;
                _cfLog.Clear();
                for (var attempt = 0; attempt < 4 && !Said("shatters"); attempt++)
                {
                    e.Effects.Apply(new StatusApplication(StatusKind.Frost, 5f, 0));
                    Shield(e);
                    AbilitiesRefill(party.Members[brand]);
                    party.Attack(brand, heavy: true);
                }
                GD.Print($"[combat-feel] 11 shatter: frost {e.Effects.Has(StatusKind.Frost)} log [{string.Join(" | ", _cfLog)}]");
                Check(Said("shatters"), "no 'shatters' line");
                Check(!e.Effects.Has(StatusKind.Frost), "the Frost survived the Shatter");
                e.Effects.Apply(new StatusApplication(StatusKind.Poison, 6f, 3f));
                _cfE2 = b.SpawnEnemy(e.Profile.Id, e.GlobalPosition + new Vector3(1.5f, 0, 0), b.CurrentFloor);
                if (_cfE2 == null) { Fail("could not spawn E2"); Finish(); return; }
                Stun(_cfE2);
                Go(23, 0.2);
                break;
            }
            case 23:
            {
                var e = _cfE1!; var e2 = _cfE2!;
                var torch = new AttackProfile { MinDamage = 1, MaxDamage = 1, Type = DamageType.Fire, Kind = AttackKind.Spell, OnHit = new[] { new StatusApplication(StatusKind.Burn, 3f, 2f) } };
                var e2Before = e2.Hp;
                var r = e.ReceiveAttack(party.Members[brand].Stats, torch, new HitContext { DefenderStunned = true }, party.Rng, "test");
                GD.Print($"[combat-feel] 11 ignite: burst {r.IgniteBurst:0} E2 hp {e2Before:0}->{e2.Hp:0} log [{string.Join(" | ", _cfLog)}]");
                Check(Math.Abs(r.IgniteBurst - 12f) < 0.01f, $"ignite burst {r.IgniteBurst}, expected 12");
                Check(e2.Hp < e2Before, "the ignition did not reach E2");
                // Bleed halves healing
                var m = party.Members[brand];
                var max = m.Stats[Stat.MaxHp];
                m.Hp = max * 0.5f;
                m.Effects.Apply(new StatusApplication(StatusKind.Bleed, 5f, 1f));
                m.Heal(10f);
                GD.Print($"[combat-feel] 11 bleed: heal 10 from {max * 0.5f:0.0} -> {m.Hp:0.0} (expected {max * 0.5f + 5f:0.0})");
                Check(Math.Abs(m.Hp - (max * 0.5f + 5f)) < 0.01f, "bleeding did not halve the heal");
                m.Effects.Remove(StatusKind.Bleed);
                // Vulnerable from a spell's stun
                var freeze = new AttackProfile { MinDamage = 1, MaxDamage = 1, Type = DamageType.Frost, Kind = AttackKind.Spell, OnHit = new[] { new StatusApplication(StatusKind.Stun, 0.7f, 0) } };
                e.ReceiveAttack(m.Stats, freeze, new HitContext { DefenderStunned = true }, party.Rng, "test");
                Check(e.Effects.Has(StatusKind.Vulnerable), "a spell's stun left no Vulnerable");
                // Frost scales the wind-up
                e.Effects.Apply(new StatusApplication(StatusKind.Frost, 5f, 0));
                var expectedWindup = e.Profile.WindupSeconds / 0.7f;
                GD.Print($"[combat-feel] 11 vulnerable {e.Effects.Has(StatusKind.Vulnerable)}; frosted windup {e.WindupSeconds:0.000} (expected {expectedWindup:0.000})");
                Check(Math.Abs(e.WindupSeconds - expectedWindup) <= 0.01f, $"frosted wind-up {e.WindupSeconds:0.000}, expected {expectedWindup:0.000}");
                Free(ref _cfE1); Free(ref _cfE2);
                AbilitiesRefill(m);
                Go(24, 0.3);
                break;
            }
            case 24: // 12. the boss and the companions
            {
                _cfBoss = b.SpawnEnemy("enemy.crypt_lord", _cfStand - Vector3.Up * 0.2f + _cfForward * 5f, b.CurrentFloor);
                if (_cfBoss == null) { Fail("could not spawn the crypt lord"); Finish(); return; }
                var boss = _cfBoss;
                for (var i = 0; i < party.Adventurers.Length; i++)
                {
                    if (i == brand) continue;
                    var a = Adv(i);
                    a.SetControl(ControlKind.Companion);
                    a.Stance = Stance.Aggressive;
                    a.ForceDodgeRoll = 0f;
                    AbilitiesRefill(party.Members[i]);
                    var side = (i % 2 == 0 ? 1f : -1f) * 2f;
                    a.Teleport(boss.GlobalPosition + new Vector3(side, 0.2f, (i == 1 ? -2f : 0f)), boss.GlobalPosition + Vector3.Up);
                }
                Adv(brand).Teleport(boss.GlobalPosition - _cfForward * 2.0f + Vector3.Up * 0.2f, boss.GlobalPosition + Vector3.Up);
                boss.ForceAggro();
                _cfDodgesBefore = DodgesOfCompanions();
                Go(25, 0.15); // a tick for the boss to pick its target
                break;
            }
            case 25:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                boss.ForceSignature();
                GD.Print($"[combat-feel] 12 boss {boss.DisplayName}: state {boss.State.Name} telegraph {boss.TelegraphRemaining:0.00} (delay {TelegraphRules.SlamDelay}) decal {DecalPresent()} dist {boss.DistanceToParty():0.0}");
                Check(boss.State.Name == "Signature:Slam", $"the boss is in {boss.State.Name}, not Signature:Slam");
                Check(Math.Abs(boss.TelegraphRemaining - TelegraphRules.SlamDelay) <= 0.1f, $"TelegraphRemaining {boss.TelegraphRemaining:0.00}");
                Check(DecalPresent(), "no TelegraphDecal under the floor root");
                _cfLog.Clear();
                Go(26, 0.4); // the boss bar polls every 0.25 s
                break;
            }
            case 26:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                GD.Print($"[combat-feel] 12 boss bar: visible {hud.BossBar.Visible} name \"{hud.BossBar.ShownName}\"");
                Check(hud.BossBar.Visible && hud.BossBar.ShownName == boss.DisplayName, "the boss bar is not showing the boss");
                Go(27, 1.0); // 1.4 s after the signature began
                break;
            }
            case 27:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                var dodges = DodgesOfCompanions() - _cfDodgesBefore;
                GD.Print($"[combat-feel] 12 slam landed: companion dodges {dodges}, decal {DecalPresent()}, boss hp {boss.Hp:0}/{boss.MaxHp:0}, log [{string.Join(" | ", _cfLog)}]");
                Check(Said("slam") || Said("no one is in reach"), "no slam line");
                Check(dodges >= 1, "no companion dodged the telegraphed slam");
                Check(!DecalPresent(), "a TelegraphDecal remained after the slam");
                foreach (var i in Enumerable.Range(0, party.Members.Length))
                {
                    AbilitiesRefill(party.Members[i]);
                    if (i == brand || i == sable) { if (i == sable) Adv(i).ForceDodgeRoll = 1f; continue; }
                    Adv(i).SetControl(ControlKind.Parked); // the ranged pair would whittle the boss down before the parry; Sable stays for the losing roll
                }
                // a heavy blow never staggers a boss
                Sure(brand);
                Adv(brand).Teleport(boss.GlobalPosition - _cfForward * 1.5f + Vector3.Up * 0.2f, boss.GlobalPosition + Vector3.Up);
                _cfTries = 0;
                Go(28, 0.2);
                break;
            }
            case 28:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                _cfEnemyHp = boss.Hp;
                AbilitiesRefill(party.Members[brand]);
                party.Attack(brand, heavy: true);
                if (boss.Hp >= _cfEnemyHp && _cfTries++ < 3) { Wait(0.15); break; }
                GD.Print($"[combat-feel] 12 heavy blow on the boss: hp {_cfEnemyHp:0}->{boss.Hp:0} staggered {boss.Staggered}");
                Check(boss.Hp < _cfEnemyHp, "the heavy blow on the boss never landed");
                Check(!boss.Staggered, "a heavy blow staggered a boss");
                _cfDodgesBefore = DodgesOfCompanions();
                boss.ForceSignature();
                Check(boss.State.Name == "Signature:Slam", $"second signature: {boss.State.Name}");
                Go(29, 1.4);
                break;
            }
            case 29:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                var dodges = DodgesOfCompanions() - _cfDodgesBefore;
                GD.Print($"[combat-feel] 12 losing roll: companion dodges {dodges}; boss hp {boss.Hp:0}; log [{string.Join(" | ", _cfLog)}]");
                Check(dodges == 0, $"companions dodged on a losing roll: {dodges}");
                // a parry staggers a boss
                for (var i = 0; i < party.Members.Length; i++) AbilitiesRefill(party.Members[i]);
                AbilitiesTakeControl(sable);
                Stand(sable, boss, 1.6f);
                _cfTries = 0;
                Go(30, 0.3);
                break;
            }
            case 30:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                if (since > 10) { Fail($"the boss never wound up at Sable (state {boss.State.Name}, dist {boss.DistanceToParty():0.0})"); Finish(); return; }
                if (boss.State is SignatureState) { Stand(sable, boss, 1.6f); break; }
                if (!WindingUp(boss, 0.2f)) break;
                _cfLog.Clear();
                Adv(sable).DebugIntent = new InputIntent { ParryPressed = true, Live = true };
                _cfSwingAt = boss.WindupLeft;
                Go(31, Tick);
                break;
            }
            case 31:
            {
                Adv(sable).DebugIntent = null;
                Go(32, Math.Max(0.05, _cfSwingAt + 0.25 - Tick));
                break;
            }
            case 32:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                GD.Print($"[combat-feel] 12 parry on the boss: staggered {boss.Staggered} log [{string.Join(" | ", _cfLog)}]");
                if (!boss.Staggered && !Said("parries") && _cfTries++ < 2) { GD.Print("[combat-feel] 12 the boss did not swing into the window; again"); Go(30, 0.1); break; }
                Check(boss.Staggered, "a parry did not stagger the boss");
                // the half tick and the flash, then the death hides the bar
                var blow = new AttackProfile { MinDamage = boss.MaxHp * 0.6f, MaxDamage = boss.MaxHp * 0.6f };
                boss.ReceiveAttack(Trap.TrapStats, blow, new HitContext { DefenderStunned = true }, party.Rng, "test");
                Go(33, 0.5);
                break;
            }
            case 33:
            {
                if (Gone(_cfBoss, "the boss")) return;
                var boss = _cfBoss!;
                GD.Print($"[combat-feel] 12 below half: hp {boss.Hp:0}/{boss.MaxHp:0} flashing {hud.BossBar.Flashing}");
                Check(hud.BossBar.Flashing, "the boss bar did not flash at the half");
                boss.Kill();
                Go(34, 1.0);
                break;
            }
            case 34:
            {
                GD.Print($"[combat-feel] 12 boss dead: bar visible {hud.BossBar.Visible}");
                Check(!hud.BossBar.Visible, "the boss bar stayed after the boss died");
                // 13. feedback on the HUD
                AbilitiesTakeControl(brand);
                var m = party.Members[brand];
                AbilitiesRefill(m);
                var max = m.Stats[Stat.MaxHp];
                m.Hp = max * 0.3f;
                m.Effects.Apply(new StatusApplication(StatusKind.Stun, 0.3f, 0)); // always hit
                var lowBefore = _cfLowHp;
                Adv(brand).ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = max * 0.2f, MaxDamage = max * 0.2f }, "test");
                GD.Print($"[combat-feel] 13 hurt: hp {m.Hp:0}/{max:0} vignette {hud.Vignette.Alpha:0.00} shaking {party.Lead.Shaking} lowHp events {_cfLowHp - lowBefore}");
                Check(hud.Vignette.Alpha > 0f, "no vignette");
                Check(party.Lead.Shaking, "no shake for a heavy hit received");
                Check(_cfLowHp - lowBefore >= 1, "no LowHp event below a quarter");
                m.Effects.Remove(StatusKind.Stun);
                party.AwardXp(100000);
                Check(hud.LevelUpBanner.Visible, "no level-up banner");
                var cat = Game.Instance.Catalog;
                var baseId = new[] { "item.longsword", "item.bone_amulet" }.FirstOrDefault(id => cat.TryBase(id, out _)) ?? cat.Bases.Keys.First();
                var rare = cat.Create(baseId); rare.Rarity = Rarity.Rare;
                party.PickUp(rare);
                Check(hud.LootToast.Visible, "no loot toast for a Rare pickup");
                var legendary = cat.Create(baseId); legendary.Rarity = Rarity.Legendary;
                var drop = party.SpawnLoot(legendary, _cfStand + new Vector3(2, 0.3f, 2));
                var beam = drop != null && drop.GetChildren().Any(c => c.Name == "Beam");
                GD.Print($"[combat-feel] 13 banner {hud.LevelUpBanner.Visible} toast {hud.LootToast.Visible} (\"{hud.LootToast.Text}\") legendary beam {beam}");
                Check(beam, "no beam over the legendary drop");
                Finish();
                break;
            }
        }
    }
}
