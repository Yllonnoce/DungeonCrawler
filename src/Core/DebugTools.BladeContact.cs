using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Enemies;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Core;

/// <summary>
/// <c>--test-blade-contact</c>: the blade that hits what it touches, in one quiet room (seed 7, lethality 1). A blow lands when the
/// blade arrives and not at the click; nothing behind the wielder or beyond the weapon's reach is cut; a forehand cuts what
/// stands ahead before what stands to the left and never what stands to the right; a failed hit roll glances instead of
/// missing; and with the blade off the old cast still lands at once. Prints [blade] lines and RESULT.
/// </summary>
public partial class DebugTools
{
    private bool _testBlade;
    private int _blStep;
    private double _blNext;
    private Enemy? _blA, _blB, _blC;
    private Vector3 _blStand;
    private ulong _blSwungMs;
    private int _blSwings;
    private bool _blListening;
    private readonly List<string> _blFails = new();
    private readonly List<string> _blLog = new();
    private readonly List<(Enemy who, ulong ms, float amount)> _blHurts = new();

    private void BlMessage(string text) => _blLog.Add(text);
    private void BlHurt(Enemy e, float amount, HitFlavor flavor, int by) => _blHurts.Add((e, Time.GetTicksMsec(), amount));

    private void RunBladeContactTest()
    {
        if (_blStep >= 99) return;
        var main = Main.Instance;
        if (main == null || main.Builder.FloorRoot is not { } root || main.Builder.Layout is not { } layout || !Game.Instance.InGame) return;
        if (_elapsed > 50) { Fail($"timed out at step {_blStep}"); Finish(); return; }
        if (_elapsed < _blNext) return;
        var party = main.Party;
        var b = main.Builder;
        var brand = Math.Max(0, Array.FindIndex(party.Members, m => m.Name == "Brand"));
        var me = party.Members[brand];
        var adv = party.Adventurers[brand];
        var forward = new Vector3(0, 0, -1);

        void Fail(string why) { _blFails.Add(why); GD.Print($"[blade] FAIL (step {_blStep}): {why}"); }
        void Check(bool ok, string what) { if (!ok) Fail(what); }
        void Finish()
        {
            if (_blListening) { EventBus.Instance.Message -= BlMessage; EventBus.Instance.EnemyHurt -= BlHurt; _blListening = false; }
            Adventurer.BladeContact = true;
            GD.Print(_blFails.Count == 0 ? "[blade] RESULT ok" : $"[blade] RESULT FAIL ({string.Join("; ", _blFails)})");
            _blStep = 99;
            _quitAfter = _elapsed + 0.5;
        }
        void Go(int step, double wait) { _blStep = step; _blNext = _elapsed + wait; }
        bool Said(string fragment) => _blLog.Any(l => l.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        void Accuracy(float bonus)
        {
            me.SkillModifiers.RemoveAll(x => x.Source == "test");
            me.SkillModifiers.Add(new StatModifier(Stat.Accuracy, ModOp.Add, bonus, "test"));
            me.Recalculate();
        }
        // a dummy that stands where it is put: + degrees to the left of straight ahead
        Enemy? Dummy(float bearingDegrees, float distance)
        {
            var regulars = b.RegularEnemies(b.CurrentFloor);
            var id = regulars.FirstOrDefault(x => b.EnemyProfile(x)?.Kind == EnemyKind.Melee && b.EnemyProfile(x)?.IsBoss != true) ?? regulars[0];
            var a = Mathf.DegToRad(bearingDegrees);
            var e = b.SpawnEnemy(id, _blStand + Vector3.Up * 0.1f + new Vector3(-Mathf.Sin(a), 0, -Mathf.Cos(a)) * distance, b.CurrentFloor);
            if (e == null) { Fail("could not spawn a dummy"); return null; }
            e.ForceAggro(); // an unaware body would be ambushed: a sure hit and a crit
            Hold(e);
            return e;
        }
        void Hold(Enemy? e)
        {
            if (e == null || !IsInstanceValid(e) || !e.IsAlive) return;
            e.Effects.Apply(new StatusApplication(StatusKind.Stun, 60f, 0));
            e.Effects.Apply(new StatusApplication(StatusKind.Shield, 120f, StatusEffectController.MaxShield));
        }
        void Free(ref Enemy? e) { if (e != null && IsInstanceValid(e)) e.QueueFree(); e = null; }
        void Swing(bool heavy = false)
        {
            Hold(_blA); Hold(_blB); Hold(_blC);
            AbilitiesRefill(me);
            adv.Teleport(_blStand, _blStand + forward * 1.5f + Vector3.Up * 1.3f); // eyes level, looking down the room
            _blHurts.Clear(); _blLog.Clear();
            _blSwungMs = Time.GetTicksMsec();
            party.Attack(brand, heavy);
        }
        int HurtsOf(Enemy? e) => _blHurts.Count(h => h.who == e);
        long DelayOf(Enemy? e) => _blHurts.Where(h => h.who == e).Select(h => (long)h.ms - (long)_blSwungMs).DefaultIfEmpty(-1).First();

        switch (_blStep)
        {
            case 0 when _elapsed >= 1.0:
            {
                foreach (var e in root.GetChildren().OfType<Enemy>()) e.QueueFree();
                EventBus.Instance.Message += BlMessage; EventBus.Instance.EnemyHurt += BlHurt;
                _blListening = true;
                AbilitiesTakeControl(brand);
                Accuracy(1000f);
                var room = layout.Rooms.OrderByDescending(r => r.W * r.H).First();
                _blStand = b.Surface(room.Center) + Vector3.Up * 0.3f;
                party.Teleport(_blStand, _blStand + forward * 1.5f);
                var style = WeaponStyles.Of(me.Weapon);
                GD.Print($"[blade] floor {b.CurrentFloor} seed {Game.Instance.GameSeed}: Brand holds {me.Weapon?.DisplayName ?? "nothing"} ({style}), range {me.WeaponProfile.Range:0.0}, blade {(Adventurer.BladeContact ? "on" : "OFF")}");
                Check(Adventurer.BladeContact, "the blade is off at the start of its own test");
                Check(style != WeaponStyle.Fists && !WeaponStyles.IsRanged(style), $"Brand's weapon is {style}: nothing to swing");
                _blA = Dummy(0f, 1.5f);
                if (_blA == null) { Finish(); return; }
                Go(1, 2.0); // let the bodies settle
                break;
            }
            case 1: // 1. the blow lands when the blade arrives
            {
                _blStand = adv.GlobalPosition; // where the floor turned out to be, once both bodies had settled on it
                Swing();
                GD.Print($"[blade] Brand at {adv.GlobalPosition:0.0}, the dummy at {_blA!.GlobalPosition:0.0}");
                Check(adv.BladeSwinging, "no blade swing started");
                Check(_blHurts.Count == 0, "the blow landed at the click");
                Go(2, 0.8);
                break;
            }
            case 2:
            {
                GD.Print($"[blade] 1 timing: hurts {HurtsOf(_blA)} after {DelayOf(_blA)} ms; swinging {adv.BladeSwinging}; log [{string.Join(" | ", _blLog)}]");
                Check(HurtsOf(_blA) == 1, $"the dummy ahead was hurt {HurtsOf(_blA)} times by one swing");
                Check(DelayOf(_blA) >= 30, $"the blow landed {DelayOf(_blA)} ms after the click");
                Check(!adv.BladeSwinging, "the swing never ended");
                Check(!Said("swings at nothing"), "a swing that cut still said 'swings at nothing'");
                // 2. behind and beyond
                Free(ref _blA);
                _blB = Dummy(180f, 1.3f);
                _blC = Dummy(0f, me.WeaponProfile.Range + 1.6f);
                Go(3, 1.2);
                break;
            }
            case 3:
            {
                Swing();
                Go(4, 0.8);
                break;
            }
            case 4:
            {
                GD.Print($"[blade] 2 behind {HurtsOf(_blB)}, beyond reach {HurtsOf(_blC)}; log [{string.Join(" | ", _blLog)}]");
                Check(HurtsOf(_blB) == 0, "a body behind the wielder was cut");
                Check(HurtsOf(_blC) == 0, "a body beyond the weapon's reach was cut");
                Check(Said("swings at nothing"), "no 'swings at nothing' line");
                // 3. a forehand: ahead, then left; never right
                Free(ref _blB); Free(ref _blC);
                _blA = Dummy(0f, 1.5f);
                _blB = Dummy(50f, 1.5f);
                _blC = Dummy(-75f, 1.5f);
                Go(5, 1.2);
                break;
            }
            case 5:
            {
                Swing(); // more than the combo window since the last swing: the forehand
                Go(6, 0.8);
                break;
            }
            case 6:
            {
                var ahead = _blHurts.FindIndex(h => h.who == _blA);
                var left = _blHurts.FindIndex(h => h.who == _blB);
                GD.Print($"[blade] 3 forehand: ahead {HurtsOf(_blA)} at {DelayOf(_blA)} ms, left {HurtsOf(_blB)} at {DelayOf(_blB)} ms, right {HurtsOf(_blC)}; log [{string.Join(" | ", _blLog)}]");
                Check(HurtsOf(_blA) == 1 && HurtsOf(_blB) == 1, $"ahead {HurtsOf(_blA)} and left {HurtsOf(_blB)}: each should be cut once");
                Check(ahead >= 0 && left > ahead, "the body to the left was cut before the one ahead");
                Check(HurtsOf(_blC) == 0, "a forehand cut the body to the right");
                // 4. a touch always lands
                Free(ref _blB); Free(ref _blC);
                Accuracy(-1000f);
                _blSwings = 0;
                _blLog.Clear();
                Go(7, 1.0);
                break;
            }
            case 7:
            {
                if (_blSwings > 0) Check(HurtsOf(_blA) == 1, $"swing {_blSwings} with no accuracy: hurts {HurtsOf(_blA)}");
                var kept = _blLog.ToList();
                if (_blSwings == 8)
                {
                    GD.Print($"[blade] 4 glancing: 8 swings; log [{string.Join(" | ", kept)}]");
                    Check(kept.Any(l => l.Contains("glances off")), "no blow glanced in 8 swings at a 35% hit chance");
                    Check(!kept.Any(l => l.Contains("misses")), "a touch missed");
                    // 5. the old cast, for the bodies that have no blade
                    Accuracy(1000f);
                    Adventurer.BladeContact = false;
                    Go(8, 1.0);
                    break;
                }
                _blSwings++;
                Swing();
                _blLog.AddRange(kept);
                Go(7, 0.8);
                break;
            }
            case 8:
            {
                Swing();
                GD.Print($"[blade] 5 blade off: hurts at once {HurtsOf(_blA)}, swinging {adv.BladeSwinging}");
                Check(HurtsOf(_blA) == 1, "with the blade off the blow did not land at once");
                Check(!adv.BladeSwinging, "with the blade off a blade swing started");
                Finish();
                break;
            }
        }
    }
}
