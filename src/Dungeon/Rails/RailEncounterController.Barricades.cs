using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 11's barricades: timber across the party line ("barricade") or the slag line ("slag_barricade", on the enemy track, its S and
/// face in enemy-lane S), cut down by <see cref="BarricadeRules.Toughness"/> hits or one
/// spell. While its ride is armed the lantern blinks as the cart bears down (the heap's rule, <see cref="SetPieceRules.HeapAlarm"/>),
/// the bell rings as it starts, and a seated companion calls it once <see cref="BarricadeRules.CallAhead"/> before its face. A cart
/// whose front reaches the face while it stands smashes through: <see cref="BarricadeRules.SmashDent"/> plating, the speed kept by
/// <see cref="BarricadeRules.SpeedKept"/>, and each seated hero who is up hurt by <see cref="BarricadeRules.SmashDamage"/> (never
/// below 1 HP); NPCs on the bench are only jolted. The party's cart judges a barricade only on that barricade's own line
/// (<see cref="BarricadeRules.OnItsLane"/>): a slag-line one once the cart is across on the enemy lane. Every other cart on its line smashes
/// one standing too (Ruling 4, <see cref="SmashAhead"/>): a ride's slag carts and the traffic cart on the enemy track, a runaway wagon on
/// the party's; a slag cart's riders each lose <see cref="BarricadeRules.SmashHpFraction"/> of their own max HP. Down, it stays down until
/// its ride is undone (a wreck or a load), or stands again at that ride's next arming when it went down while its ride was not armed
/// (<see cref="BarricadeRules.StandsAgainAtArming"/>), as the heap does. Judged on the server; a client's copy blinks its lantern by the same rule.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>Barricades cut down and smashed through this floor build (tests read them).</summary>
    public int BarricadesCut { get; private set; }
    public int BarricadesSmashed { get; private set; }
    private readonly List<Adventurer> _smashedHeroes = new();
    private readonly List<Enemy> _smashedRiders = new();

    // ---- building --------------------------------------------------------------------------------

    /// <summary>A barricade across its line at the piece's place: the party line's at its S, the slag line's at the enemy-lane S beside it.</summary>
    private Node3D? BuildBarricade(Node3D root, Piece piece, bool slagLine)
    {
        if (slagLine && !_enemy.Valid) return null;
        var s = slagLine ? Lanes.EnemyS(piece.S) : piece.S;
        var barricade = new Barricade { Name = $"Barricade_{piece.Plan.Id}", S = s, SlagLine = slagLine, NetRef = NetRefs.Barricade(piece.Index) };
        root.AddChild(barricade);
        barricade.GlobalTransform = (slagLine ? _enemy : _rails.Party.Track).Frame(s).Orthonormalized(); // facing the carts up its line: its +Z toward lower S
        barricade.Down += (b, how) => OnBarricadeDown(piece, b, how);
        piece.Barricade = barricade;
        GD.Print($"[rails] barricade {piece.Plan.Id} on the {(slagLine ? "slag" : "party")} line at S={s:0.0}{(slagLine ? $" (party S={piece.S:0.0})" : "")} (ride {piece.Plan.Ride})");
        return barricade;
    }

    // ---- arming and undoing ----------------------------------------------------------------------

    /// <summary>Ride <paramref name="ride"/> arms: its call may come again, and a barricade its ride's undoing left down, or one smashed while its ride was not armed, stands again (Ruling 5).</summary>
    private void ArmBarricades(int ride)
    {
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != ride || p.Barricade is not { } barricade) continue;
            p.Barked = false;
            if (!barricade.IsStanding && BarricadeRules.StandsAgainAtArming(p.DownArmed, p.Rebuild))
            {
                if (!p.Rebuild) GD.Print($"[rails] barricade {p.Plan.Id} stands again at ride {ride}'s arming (it went down while its ride was not armed)");
                barricade.Restand();
            }
            p.Rebuild = false;
            barricade.Alarm = false;
        }
    }

    /// <summary>The ride's live pieces stop: no lantern blinks.</summary>
    private void ClearBarricades()
    {
        foreach (var p in _pieces) if (p.Barricade is { } barricade) barricade.Alarm = false;
    }

    private void OnBarricadeDown(Piece piece, Barricade barricade, string how)
    {
        // a cut is the party's work wherever it is done; a smash while the ride is not armed (traffic, a runaway) stands again at its arming
        piece.DownArmed = how == "cut" || ArmedRide == piece.Plan.Ride;
        if (how != "cut") return; // a smash is told where it is judged (SmashBarricade, SmashByCart)
        BarricadesCut++;
        GD.Print($"[rails] barricade {piece.Plan.Id} cut by {barricade.CutBy} at S={_rails.Party.FrontS:0.0}");
    }

    // ---- ticking ---------------------------------------------------------------------------------

    /// <summary>
    /// The party's cart against a barricade, in the barricade's own line's S: its front this step and the last (each following the
    /// cart's way of travel), and whether the cart runs on that line. A party-line barricade is on the cart's lane when its face is
    /// not across on the enemy lane; a slag-line one when the cart's front is across (<see cref="RailController.FrontOnEnemyLane"/>).
    /// </summary>
    private (float PrevFront, float Front, bool OnItsLane) PartyFrontAt(Barricade barricade)
    {
        var run = _rails.Party;
        float prevFront = BarricadeRules.FrontOf(run.Dir, run.PrevS), front = run.FrontS;
        if (!barricade.SlagLine) return (prevFront, front, BarricadeRules.OnItsLane(false, _rails.OnEnemyLane(barricade.FaceS)));
        return (Lanes.EnemyS(prevFront), Lanes.EnemyS(front), BarricadeRules.OnItsLane(true, _rails.FrontOnEnemyLane));
    }

    /// <summary>
    /// The armed ride's barricade, each tick: the lantern blinks while the party's cart bears down on it on its line, its bell at the
    /// alarm's start; a seated hero calls it once 35 m out; the front reaching its face while it stands smashes through.
    /// </summary>
    private void TickBarricade(Piece piece, PartyController party, bool rolling)
    {
        if (piece.Barricade is not { IsStanding: true } barricade) return;
        var (prevFront, front, onItsLane) = PartyFrontAt(barricade);
        var face = barricade.FaceS;
        SetBarricadeAlarm(barricade, SetPieceRules.HeapAlarm(SetPieceRules.HeapHeaded(rolling, face, front, !onItsLane, false), face, front, _rails.State.V));
        if (rolling && BarricadeRules.Calls(true, onItsLane, piece.Barked, front, face))
        {
            piece.Barked = true;
            var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.Barricade, "Timber across the line — cut it!");
            GD.Print($"[rails] barricade {piece.Plan.Id} called at front S={front:0.0} ({face - front:0.0} m to its face{(barricade.SlagLine ? ", on the slag line" : "")}); bark={by ?? "none"}");
        }
        if (rolling && BarricadeRules.SmashesAlong(true, onItsLane, _rails.Party.Dir, prevFront, front, barricade.S)) SmashBarricade(piece, barricade);
    }

    /// <summary>The lantern blinks or not, the bell ringing at the barricade as it starts (every role, by the same rule).</summary>
    private static void SetBarricadeAlarm(Barricade barricade, bool alarm)
    {
        if (barricade.Alarm == alarm) return;
        barricade.Alarm = alarm;
        if (alarm) RideAudio.At("cart_bell", barricade.SoundPoint);
    }

    /// <summary>The party's cart smashes through a standing barricade: it goes in splinters; the plating, the speed and the seated heroes pay.</summary>
    private void SmashBarricade(Piece piece, Barricade barricade)
    {
        if (GetParent() is not Node3D root) return;
        var before = _rails.State.Plating;
        EventBus.Instance.Log("The cart smashes through the barricade!");
        barricade.Smash(root, _rails.Cart.Velocity);
        BarricadesSmashed++;
        var hit = SmashCost("The barricade", wholeTrain: false); // the cart's seats only, as plan 11 has it (pre-flight F4)
        GD.Print($"[rails] barricade {piece.Plan.Id} smashed at S={piece.S:0.0}: plating {before:0} -> {_rails.State.Plating:0}, {hit} hero(es) hit");
    }

    /// <summary>
    /// The cost of the party's train smashing through something on its line (Ruling 24; one helper for <c>r1_barricade</c>, <c>r2_barricade</c>
    /// and the parked train): <see cref="BarricadeRules.SmashDent"/> plating, <see cref="BarricadeRules.SpeedKept"/> of the speed kept, and
    /// <see cref="BarricadeRules.SmashDamage"/> (15% × lethality/3 of max HP, never below 1 HP) to each seated hero who is up: in the cart only,
    /// or anywhere in the train with <paramref name="wholeTrain"/> (pre-flight F4: the parked train's reach). Crew and escorts are only jolted.
    /// A wreck the dent makes has its own toll: then nobody is hurt here. <paramref name="what"/> names the source. Returns the heroes hurt. Server.
    /// </summary>
    internal int SmashCost(string what, bool wholeTrain)
    {
        _rails.Smash(BarricadeRules.SmashDent, BarricadeRules.SpeedKept);
        if (_rails.State.Mode == CartMode.Wrecked) return 0; // a wreck has its own toll
        _smashedHeroes.Clear();
        void Book(ICartBody car)
        {
            foreach (var rider in car.Riders.Values) // the book first: a hit must not change it under the loop
                if (rider is Adventurer { Downed: false } hero && IsInstanceValid(hero) && !_smashedHeroes.Contains(hero)) _smashedHeroes.Add(hero);
        }
        Book(_rails.Cart);
        if (wholeTrain)
            foreach (var wagon in _rails.Train)
                if (IsInstanceValid(wagon)) Book(wagon);
        var hit = 0;
        var scale = TierRules.PartyDamageScale;
        foreach (var hero in _smashedHeroes)
        {
            var maxHp = hero.State.Stats[Stat.MaxHp];
            var damage = BarricadeRules.SmashDamage(maxHp, hero.State.Hp, scale);
            if (damage <= 0f || maxHp <= 0f) continue;
            hero.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, damage / maxHp, 0f), what, -1);
            hit++;
        }
        _smashedHeroes.Clear();
        return hit;
    }

    // ---- the other carts on a barricade's line ----------------------------------------------------

    /// <summary>
    /// A cart other than the party's, after its step (a ride's slag cart and the traffic cart on the enemy track, <paramref name="enemyTrack"/>;
    /// a runaway wagon on the party's): every standing barricade on its own line whose face its front reached this step, read along its
    /// own way of travel, is smashed (<see cref="SmashByCart"/>), on any ride. No allocation.
    /// </summary>
    private void SmashAhead(CartRun run, Node3D cart, bool enemyTrack, List<Enemy>? riders)
    {
        if (_pieces.Count == 0 || run.State.Mode != CartMode.Rolling) return;
        var prevFront = BarricadeRules.FrontOf(run.Dir, run.PrevS);
        var front = run.FrontS;
        foreach (var p in _pieces)
        {
            if (p.Barricade is not { IsStanding: true } barricade || !IsInstanceValid(barricade)) continue;
            if (BarricadeRules.SmashesAlong(true, BarricadeRules.OnItsLane(barricade.SlagLine, enemyTrack), run.Dir, prevFront, front, barricade.S))
                SmashByCart(p, barricade, run, cart, riders);
        }
    }

    /// <summary>
    /// <paramref name="cart"/> smashes through a standing barricade on its line: it goes in splinters, the run keeps <see cref="BarricadeRules.SpeedKept"/>
    /// of its speed, and each living rider takes <see cref="BarricadeRules.SmashHpFraction"/> of its own max HP (not scaled by lethality: Ruling 4;
    /// a rider may die of it). A runaway's is logged.
    /// </summary>
    private void SmashByCart(Piece piece, Barricade barricade, CartRun run, Node3D cart, List<Enemy>? riders)
    {
        if (GetParent() is not Node3D root) return;
        if (cart is RunawayWagon) EventBus.Instance.Log("The runaway smashes through the barricade!");
        barricade.Smash(root, -cart.GlobalBasis.Z * run.State.V); // the body's −Z is its way of travel
        run.State = run.State with { V = run.State.V * BarricadeRules.SpeedKept };
        var hit = 0;
        if (riders != null && riders.Count > 0)
        {
            _smashedRiders.Clear();
            foreach (var rider in riders) // the book first: a death must not change it under the loop
                if (IsInstanceValid(rider) && rider.IsAlive) _smashedRiders.Add(rider);
            var rng = Main.Instance.Party.Rng;
            foreach (var rider in _smashedRiders)
            {
                var damage = rider.MaxHp * BarricadeRules.SmashHpFraction;
                if (damage <= 0f) continue;
                rider.ReceiveAttack(Trap.TrapStats, new AttackProfile { MinDamage = damage, MaxDamage = damage }, new HitContext { DefenderStunned = true }, rng, "The barricade");
                hit++;
            }
            _smashedRiders.Clear();
        }
        GD.Print($"[rails] {cart.Name} smashes the barricade {piece.Plan.Id} at S={barricade.S:0.0}: {hit} rider(s) hit");
    }

    // ---- test hooks ------------------------------------------------------------------------------

    /// <summary>Server test hook (Ruling 24): ride <paramref name="ride"/>'s standing barricades are cut at once, by the test.</summary>
    internal void DebugCutBarricades(int ride)
    {
        foreach (var p in _pieces)
            if (p.Plan.Ride == ride && p.Barricade is { IsStanding: true } barricade) barricade.DebugCut("The test");
    }

    /// <summary>Server test hook: ride <paramref name="ride"/>'s barricades stand again, whole.</summary>
    internal void DebugRestandBarricades(int ride)
    {
        foreach (var p in _pieces)
            if (p.Plan.Ride == ride && p.Barricade is { } barricade) barricade.Restand();
    }
}
