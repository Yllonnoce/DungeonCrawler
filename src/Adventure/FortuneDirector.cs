using DungeonCrawler.Adventure.Fortune;
using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Core;
using DungeonCrawler.Dungeon;
using DungeonCrawler.Enemies;
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
        bus.FloorBuilt += (_, _, _) => { Refresh(); OnFloorStart(); };
        bus.RosterChanged += Refresh;
        bus.BoonTaken += _ => Refresh();
        bus.CurseTaken += _ => Refresh();
        bus.PerfectBlock += BlockHeal;
        bus.Parried += BlockHeal;
        bus.EliteSlain += EliteRewards;
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
