using DungeonCrawler.Adventure;
using DungeonCrawler.Characters;
using DungeonCrawler.Core;
using DungeonCrawler.Net;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// Plan 11's barricades: timber across the party line ("barricade"), cut down by <see cref="BarricadeRules.Toughness"/> hits or one
/// spell. While its ride is armed the lantern blinks as the cart bears down (the heap's rule, <see cref="SetPieceRules.HeapAlarm"/>),
/// the bell rings as it starts, and a seated companion calls it once <see cref="BarricadeRules.CallAhead"/> before its face. A cart
/// whose front reaches the face while it stands smashes through: <see cref="BarricadeRules.SmashDent"/> plating, the speed kept by
/// <see cref="BarricadeRules.SpeedKept"/>, and each seated hero who is up hurt by <see cref="BarricadeRules.SmashDamage"/> (never
/// below 1 HP); NPCs on the bench are only jolted. Down, it stays down until its ride is undone (a wreck or a load): it stands again
/// at that ride's next arming, as the heap does. Judged on the server; a client's copy blinks its lantern by the same rule.
/// </summary>
public partial class RailEncounterController
{
    /// <summary>Barricades cut down and smashed through this floor build (tests read them).</summary>
    public int BarricadesCut { get; private set; }
    public int BarricadesSmashed { get; private set; }
    private readonly List<Adventurer> _smashedHeroes = new();

    // ---- building --------------------------------------------------------------------------------

    private Node3D? BuildBarricade(Node3D root, Piece piece)
    {
        var barricade = new Barricade { Name = $"Barricade_{piece.Plan.Id}", S = piece.S, NetRef = NetRefs.Barricade(piece.Index) };
        root.AddChild(barricade);
        barricade.GlobalTransform = _rails.Party.Track.Frame(piece.S).Orthonormalized(); // facing the cart: its +Z toward lower S
        barricade.Down += (b, how) => OnBarricadeDown(piece, b, how);
        piece.Barricade = barricade;
        GD.Print($"[rails] barricade {piece.Plan.Id} on the {(barricade.SlagLine ? "slag" : "party")} line at S={piece.S:0.0} (ride {piece.Plan.Ride})");
        return barricade;
    }

    // ---- arming and undoing ----------------------------------------------------------------------

    /// <summary>Ride <paramref name="ride"/> arms: its call may come again, and a barricade its ride's undoing left down stands again (Ruling 5).</summary>
    private void ArmBarricades(int ride)
    {
        foreach (var p in _pieces)
        {
            if (p.Plan.Ride != ride || p.Barricade is not { } barricade) continue;
            p.Barked = false;
            if (p.Rebuild && !barricade.IsStanding) barricade.Restand();
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
        if (how != "cut") return; // a smash is told where it is judged (SmashBarricade)
        BarricadesCut++;
        GD.Print($"[rails] barricade {piece.Plan.Id} cut by {barricade.CutBy} at S={_rails.Party.FrontS:0.0}");
    }

    // ---- ticking ---------------------------------------------------------------------------------

    /// <summary>
    /// The armed ride's barricade, each tick: the lantern blinks while the party's cart bears down on it, its bell at the alarm's
    /// start; a seated hero calls it once 35 m out; the front reaching its face while it stands smashes through.
    /// </summary>
    private void TickBarricade(Piece piece, PartyController party, bool rolling)
    {
        if (piece.Barricade is not { IsStanding: true } barricade) return;
        var front = _rails.Party.FrontS;
        var face = barricade.FaceS;
        var onItsLane = !_rails.OnEnemyLane(face);
        SetBarricadeAlarm(barricade, SetPieceRules.HeapAlarm(SetPieceRules.HeapHeaded(rolling, face, front, !onItsLane, false), face, front, _rails.State.V));
        if (rolling && BarricadeRules.Calls(true, onItsLane, piece.Barked, front, face))
        {
            piece.Barked = true;
            var by = RailCalls.Say(party, _rails.Cart, BarkTrigger.Barricade, "Timber across the line — cut it!");
            GD.Print($"[rails] barricade {piece.Plan.Id} called at front S={front:0.0} ({face - front:0.0} m to its face); bark={by ?? "none"}");
        }
        var prevFront = _rails.Party.PrevS + CartRules.HalfLength;
        if (rolling && BarricadeRules.Smashes(true, onItsLane, prevFront, front, face)) SmashBarricade(piece, barricade);
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
        _rails.Smash(BarricadeRules.SmashDent, BarricadeRules.SpeedKept);
        var hit = 0;
        if (_rails.State.Mode != CartMode.Wrecked) // a wreck has its own toll
        {
            _smashedHeroes.Clear();
            foreach (var rider in _rails.Cart.Riders.Values) // the book first: a hit must not change it under the loop
                if (rider is Adventurer { Downed: false } hero && IsInstanceValid(hero)) _smashedHeroes.Add(hero);
            var scale = TierRules.PartyDamageScale;
            foreach (var hero in _smashedHeroes)
            {
                var maxHp = hero.State.Stats[Stat.MaxHp];
                var damage = BarricadeRules.SmashDamage(maxHp, hero.State.Hp, scale);
                if (damage <= 0f || maxHp <= 0f) continue;
                hero.ReceiveHazard(HazardDamage.RunOver, new HazardOutcome(false, damage / maxHp, 0f), "The barricade", -1);
                hit++;
            }
        }
        GD.Print($"[rails] barricade {piece.Plan.Id} smashed at S={piece.S:0.0}: plating {before:0} -> {_rails.State.Plating:0}, {hit} hero(es) hit");
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
