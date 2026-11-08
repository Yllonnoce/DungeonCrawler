using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Dungeon.Props;
using DungeonCrawler.Enemies;
using DungeonCrawler.Net;
using DungeonCrawler.Items;
using DungeonCrawler.Party;
using DungeonCrawler.Save;
using Godot;

namespace DungeonCrawler.Adventure;

/// <summary>
/// The one node that makes boons and curses real: it rewrites every member's <see cref="CharacterState.FortuneModifiers"/>,
/// the static factors (prices, hazards, potions), hands out the per-floor wards, and runs the event-driven boons
/// (Ember's Due, Kindled Blades, Rime Edge, Stone Crown, Toll Crown). The shrine card, the hunt and the secret-room
/// detection live here too (later tasks). A child of <see cref="Game"/>; the server (or solo) is the authority.
/// </summary>
public partial class FortuneDirector : Node
{
    public static FortuneDirector? Instance { get; private set; }

    private static bool Authority => !Net.NetSession.IsClient;
    private static AdventureDto Adv => Game.Instance.Adventure;
    private static PartyController? Party => Main.Instance is { } main && main.InGame ? main.Party : null;

    public override void _Ready()
    {
        Instance = this;
        var bus = EventBus.Instance;
        bus.FloorBuilt += (_, _, _) => { _pendingShrine = null; Refresh(); OnFloorStart(); };
        bus.RosterChanged += Refresh;
        bus.BoonTaken += _ => Refresh();
        bus.CurseTaken += _ => Refresh();
        bus.PerfectBlock += BlockHeal;
        bus.Parried += BlockHeal;
        bus.EliteSlain += EliteRewards;
        bus.EnemyDied += OnEnemyDied;
    }

    private void OnEnemyDied(Enemy e)
    {
        if (!Authority || Party is not { } party || Main.Instance?.Builder.FloorRoot is not { } root) return;
        foreach (var chest in root.GetChildren().OfType<Chest>())
            if (chest.Mimic == e) chest.Yield(party);
    }

    public override void _ExitTree() { if (Instance == this) Instance = null; }

    /// <summary>Pushes the held boons and curses into every member's stats and the static factors. Safe to call any time.</summary>
    public void Refresh()
    {
        var adv = Adv;
        var mods = FortuneRules.StatModifiers(adv).ToList();
        if (Party is { } party)
            foreach (var m in party.Members)
            {
                m.FortuneModifiers.Clear();
                m.FortuneModifiers.AddRange(mods);
                m.Effects.DurationScale = k => FortuneRules.DurationScale(Game.Instance.Adventure, k);
                m.Recalculate();
                if (m.IsAlive) m.Hp = Math.Min(m.Hp, m.Stats[Stat.MaxHp]);
            }
        Prices.BuyFactor = FortuneRules.PriceFactor(adv);
        TierRules.HazardFactor = FortuneRules.HazardFactor(adv);
        ConsumableRules.HealFactor = FortuneRules.HealFactor(adv);
        if (Party != null) for (var i = 0; i < Party.Members.Length; i++) EventBus.Instance.RaiseMemberChanged(i);
        if (NetSession.IsServer) NetSession.Instance.Server?.Broadcast(new FortuneSync(adv.Boons.ToList(), adv.Curses.ToList()));
    }

    // ---- secrets found -----------------------------------------------------------------------------------

    private readonly HashSet<int> _found = new();
    private int _foundFloor = int.MinValue;
    private double _secretTick;
    private const double SecretTickSeconds = 0.25;
    /// <summary>Secret indices found on the current floor: rooms entered, alcove walls opened. Saved per floor.</summary>
    public IReadOnlyCollection<int> FoundSecrets => _found;

    /// <summary>A reload: the floor's found secrets, before FloorBuilt fires (a first visit starts empty).</summary>
    public void RestoreSecrets(IEnumerable<int> indices)
    {
        _found.Clear();
        foreach (var i in indices) _found.Add(i);
        _foundFloor = Game.Instance.CurrentFloor;
    }

    public override void _Process(double delta)
    {
        if (!Authority || Main.Instance is not { } main || !main.InGame || main.Builder.Layout is not { } layout || main.Builder.FloorRoot is not { } root) return;
        _secretTick -= delta;
        if (_secretTick > 0) return;
        _secretTick = SecretTickSeconds;
        var floor = Game.Instance.CurrentFloor;
        if (_foundFloor != floor) { _found.Clear(); _foundFloor = floor; }
        if (layout.Secrets.Count == 0) return;
        var cells = main.Party.Active.Where(a => !a.Downed && a.State.IsAlive).Select(a => DungeonBuilder.WorldToCell(a.GlobalPosition)).ToList();
        foreach (var s in layout.Secrets)
        {
            if (_found.Contains(s.Index)) continue;
            var room = layout.SecretRooms.FirstOrDefault(r => r.SecretIndex == s.Index);
            var found = room != null ? cells.Any(c => room.Room.Contains(c) || c == room.Doorway)
                : root.GetChildren().OfType<SecretWall>().FirstOrDefault(w => w.Index == s.Index)?.IsOpen == true;
            if (!found) continue;
            _found.Add(s.Index);
            EventBus.Instance.Log(room != null ? "A room nobody was meant to find." : "A hollow behind the stones.");
            EventBus.Instance.RaiseSecretFound(floor);
        }
    }

    // ---- the hunt ----------------------------------------------------------------------------------------

    /// <summary>Play-seconds until which the party is Hunted; the status itself runs the clock.</summary>
    public double HuntUntil { get; private set; }

    /// <summary>The bound chest opened: every living member Vulnerable for <see cref="FortuneRules.HuntedSeconds"/>, and a hunter at the nearest other room, coming.</summary>
    public void StartHunt(PartyController party, Vector3 at)
    {
        if (!Authority || Main.Instance?.Builder is not { } builder || builder.Layout is not { } layout) return;
        var floor = Game.Instance.CurrentFloor;
        for (var i = 0; i < party.Members.Length; i++)
        {
            var m = party.Members[i];
            if (!m.IsAlive) continue;
            m.Effects.Apply(new StatusApplication(StatusKind.Vulnerable, FortuneRules.HuntedSeconds, FortuneRules.VulnerableMagnitude));
            m.Recalculate();
            EventBus.Instance.RaiseMemberChanged(i);
        }
        HuntUntil = Game.Instance.PlaySeconds + FortuneRules.HuntedSeconds;
        var chestCell = DungeonBuilder.WorldToCell(at);
        var own = layout.RoomAt(chestCell);
        var room = layout.Rooms.Where(r => r != own && layout.Vaults.All(v => v.Room != r) && !layout.IsInSafeRoom(r.Center)).OrderBy(r => Dungeon.Generation.FloorGenerator.Dist2(r.Center, chestCell)).FirstOrDefault() ?? own;
        var pool = builder.RegularEnemies(floor);
        if (room != null && pool.Count > 0)
        {
            var rng = new Random(Rng.ActorSeed(Game.Instance.GameSeed, floor, FortuneRules.HunterActor));
            var id = pool[rng.Next(pool.Count)];
            FortuneSpawns.Spawn(builder, id, builder.Surface(room.Center) + Vector3.Up * 0.1f, floor, FortuneRules.HunterName, FortuneRules.HunterAffixes, aggro: true);
        }
        EventBus.Instance.PlaySfx("alarm");
        EventBus.Instance.Log("Something has your scent.");
    }

    // ---- the shrine --------------------------------------------------------------------------------------

    private ShrineCardMsg? _pendingShrine;
    private int _pendingFloor;
    /// <summary>The card the authority is waiting on; null when none is open.</summary>
    public ShrineCardMsg? PendingShrine => _pendingShrine;

    /// <summary>Run-structure's Fine Print clause (two offers) read without a compile-time dependency: the field may not exist yet.</summary>
    private static bool FinePrint(AdventureDto adv)
        => adv.GetType().GetProperty("Clauses")?.GetValue(adv) is IEnumerable<string> clauses && clauses.Contains("fine_print");

    /// <summary>A body touched the altar: pay the price (or refuse, unused), mark it used, and open the card (solo) or broadcast it (server).</summary>
    public void OpenShrine(Shrine shrine, PartyController party, Adventurer actor)
    {
        if (!Authority || shrine.Used || _pendingShrine != null) return;
        var floor = Game.Instance.CurrentFloor;
        var adv = Adv;
        if (shrine.Price == ShrineRules.Price.Gold && !ShrineRules.CanPay(shrine.Price, party.Gold, floor))
        {
            EventBus.Instance.Log($"The altar wants {ShrineRules.GoldPrice(floor)} gold you do not have.");
            return;
        }
        switch (shrine.Price)
        {
            case ShrineRules.Price.Blood:
                for (var i = 0; i < party.Members.Length; i++)
                {
                    var m = party.Members[i];
                    if (!m.IsAlive) continue;
                    m.Hp = Math.Max(1f, m.Hp - m.Hp * ShrineRules.BloodFraction); // straight off the breath: no ward, no negation
                    EventBus.Instance.RaiseMemberChanged(i);
                }
                EventBus.Instance.Log("The altar drinks a quarter of every breath.");
                break;
            case ShrineRules.Price.Gold:
                party.TrySpendGold(ShrineRules.GoldPrice(floor));
                break;
            case ShrineRules.Price.Curse:
                if (FortuneRules.RandomCurse(adv, party.Rng) is { } curse) TakeCurse(curse, actor.Index);
                else EventBus.Instance.Log("The altar finds nothing left to lay on you.");
                break;
        }
        shrine.MarkUsed();
        var count = shrine.Price == ShrineRules.Price.Curse ? ShrineRules.OfferCountCursed : FinePrint(adv) ? ShrineRules.OfferCountFinePrint : ShrineRules.OfferCount;
        var offers = ShrineRules.Offer(Game.Instance.GameSeed, floor, adv.Boons, count);
        var lift = adv.Curses.FirstOrDefault(c => c != Curses.Scar);
        var card = new ShrineCardMsg(offers, lift, ShrineRules.LiftPrice(floor), ShrineRules.SkipGold(floor), ShrineRules.PriceLine(shrine.Price, floor), actor.Index);
        _pendingShrine = card;
        _pendingFloor = floor;
        if (NetSession.IsServer) NetSession.Instance.Server?.Broadcast(card);
        else Main.Instance.Hud.Shrine.Open(card);
    }

    /// <summary>The pick (the first one, in co-op): a boon, walking away for the skip gold, or lifting a curse for its price. Closes the card everywhere.</summary>
    public void ResolveShrine(int member, int choice)
    {
        if (!Authority || _pendingShrine is not { } card || Party is not { } party) return;
        var floor = _pendingFloor;
        string outcome;
        if (choice >= 0 && choice < card.Boons.Count) { TakeBoon(card.Boons[choice], member); outcome = "boon:" + card.Boons[choice]; }
        else if (choice == -1)
        {
            party.AddGold(card.SkipGold);
            EventBus.Instance.Log($"You leave the altar alone. It pays you {card.SkipGold} gold to go.");
            outcome = "skip";
        }
        else if (choice == -2 && card.LiftCurse is { } lift)
        {
            if (!LiftCurse(lift, member, card.LiftPrice)) return; // short: the card stays open
            outcome = "lift:" + lift;
        }
        else return;
        _pendingShrine = null;
        if (NetSession.IsServer)
        {
            NetSession.Instance.Server?.Broadcast(new ShrineClosedEvt(outcome));
            NetSession.Instance.Server?.Broadcast(new FortuneSync(Adv.Boons.ToList(), Adv.Curses.ToList()));
        }
        else Main.Instance.Hud.Shrine.Close(outcome);
    }

    /// <summary>Per-floor wards: Pennies for the Ferryman and Second Skin are refilled on every floor build.</summary>
    public void OnFloorStart()
    {
        if (Party is not { } party) return;
        var ferryman = Boons.Has(Adv, "ferryman");
        var secondSkin = Boons.Has(Adv, "second_skin");
        foreach (var m in party.Members)
        {
            m.DeathWards = ferryman ? 1 : 0;
            m.NegateHits = secondSkin ? 1 : 0;
        }
    }

    /// <summary>The on-hit boons, from <see cref="PartyController.OnHitLanded"/> (authority only; a client's hits are cosmetic).</summary>
    public void OnHit(CharacterState m, ICombatTarget target, DamageResult r)
    {
        if (!Authority || !r.Hit || Party is not { } party) return;
        var adv = Adv;
        if (target is Enemy e && Boons.Has(adv, "ember_due") && r.Applied.Any(a => a.Kind == StatusKind.Burn)) e.Effects.Extend(StatusKind.Burn, FortuneRules.EmberDueExtraSeconds);
        if (!target.IsAlive) return;
        if (Boons.Has(adv, "kindled_blades") && !m.WeaponProfile.IsRanged && r.Type == DamageType.Physical && target is Enemy && party.Rng.NextDouble() < FortuneRules.KindledBladesChance)
        {
            target.ApplyStatus(new StatusApplication(StatusKind.Burn, FortuneRules.KindledBurnSeconds, FortuneRules.KindledBurnMagnitude));
            EventBus.Instance.Log($"{m.Name}'s blade catches: {target.DisplayName} burns.");
        }
        if (Boons.Has(adv, "rime_edge") && r.Crit) target.ApplyStatus(new StatusApplication(StatusKind.Frost, FortuneRules.RimeFrostSeconds, 1f));
    }

    private void BlockHeal(int member)
    {
        if (!Authority || Party is not { } party || !Boons.Has(Adv, "stone_crown")) return;
        if (member < 0 || member >= party.Members.Length || !party.Members[member].IsAlive) return;
        party.Members[member].Heal(FortuneRules.BlockHealAmount);
        EventBus.Instance.RaiseMemberChanged(member);
    }

    private void EliteRewards(Enemy e)
    {
        if (!Authority || Party is not { } party || !Boons.Has(Adv, "toll_crown") || !IsInstanceValid(e)) return;
        var floor = Game.Instance.CurrentFloor;
        party.AwardXp(e.Profile.ScaledXp(floor));
        party.SpawnCoins(CoinRules.ChestCoins(floor, party.Rng), e.GlobalPosition + Vector3.Up * 0.2f);
        EventBus.Instance.Log("The Toll takes its second payment.");
    }

    /// <summary>The altar gives: the party holds the boon from now on. Raises <c>BoonTaken</c> then <c>ShrineUsed(member, true)</c>.</summary>
    public void TakeBoon(string id, int member)
    {
        var def = Boons.Get(id);
        FortuneRules.Take(Adv, id);
        EventBus.Instance.Log($"The altar gives: {def.Name}. {def.Text}");
        EventBus.Instance.RaiseBoonTaken(id);
        EventBus.Instance.RaiseShrineUsed(member, true);
    }

    /// <summary>The altar takes: a curse the party carries until lifted. Raises <c>CurseTaken</c> then <c>ShrineUsed(member, false)</c>.</summary>
    public void TakeCurse(string id, int member)
    {
        var def = Curses.Get(id);
        FortuneRules.Curse(Adv, id);
        EventBus.Instance.Log($"The altar takes: {def.Name}. {def.Text}");
        EventBus.Instance.RaiseCurseTaken(id);
        EventBus.Instance.RaiseShrineUsed(member, false);
    }

    /// <summary>Pays the price and lifts the curse; false (nothing spent) when the purse is short or the curse is not carried.</summary>
    public bool LiftCurse(string id, int member, int price)
    {
        if (Party is not { } party || !Adv.Curses.Contains(id)) return false;
        var def = Curses.Get(id);
        if (!party.TrySpendGold(price)) { EventBus.Instance.Log($"Lifting {def.Name} costs {price} gold."); return false; }
        FortuneRules.Lift(Adv, id);
        EventBus.Instance.Log($"The altar lifts {def.Name}.");
        Refresh();
        EventBus.Instance.RaiseShrineUsed(member, true);
        return true;
    }
}
