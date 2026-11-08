using DungeonCrawler.Characters;
using DungeonCrawler.Items;
using DungeonCrawler.Save;

namespace DungeonCrawler.Adventure.Fortune;

/// <summary>The four houses a boon belongs to: the dungeon is a drowned god's plumbing, and these are what it still runs on.</summary>
public enum House { Ember, Cold, Toll, Stone }

/// <summary>What a boon does beyond plain stat modifiers; the <c>FortuneDirector</c> and <see cref="FortuneRules"/> read these.</summary>
public enum FortuneHook { None, ExtendBurn, KindledBlades, RimeEdge, IgniteNova, ShatterChain, GoldFound, DeathWard, LockBonus, EliteRewards, GasThin, NegateFirstHit, BlockHeal, FrostOnSelfHalf, DotOnSelfHalf }

/// <summary>One boon: id, house, the line on the card, and how it reaches play (modifiers, a hook, a trigger).</summary>
public sealed record BoonDef(string Id, House House, string Name, string Text, bool Crown, string[] Modifiers, FortuneHook Hook = FortuneHook.None, TriggerKind Trigger = TriggerKind.None, float TriggerMagnitude = 0f);

/// <summary>The 24 boons: six per house, one crown each (offered only when the party holds two of the house).</summary>
public static class Boons
{
    private static readonly string[] None = Array.Empty<string>();

    public static readonly IReadOnlyList<BoonDef> All = new BoonDef[]
    {
        // the Ember
        new("ember_due", House.Ember, "Ember's Due", "Fire you set lingers: Burn you apply lasts 3 s longer.", false, None, FortuneHook.ExtendBurn),
        new("kindled_blades", House.Ember, "Kindled Blades", "One melee blow in five catches: 20% to Burn.", false, None, FortuneHook.KindledBlades),
        new("ember_heart", House.Ember, "Ember Heart", "The furnace in the chest: +15% melee damage and spell power.", false, new[] { "MeleeDamage*1.15", "SpellPower*1.15" }),
        new("ash_tongue", House.Ember, "Ash Tongue", "Speak to the fire and it listens: +25 fire resistance.", false, new[] { "ResistFire+25" }),
        new("quick_match", House.Ember, "Quick Match", "Strike while it is lit: +10% attack speed.", false, new[] { "AttackSpeed*1.10" }),
        new("ember_crown", House.Ember, "Ember Crown", "Ignite bursts 4 m wide, not 2.", true, None, FortuneHook.IgniteNova),
        // the Cold
        new("cold_comfort", House.Cold, "Cold Comfort", "Frost on you lasts half as long.", false, None, FortuneHook.FrostOnSelfHalf),
        new("rime_edge", House.Cold, "Rime Edge", "Critical hits leave Frost for 2 s.", false, None, FortuneHook.RimeEdge),
        new("still_hands", House.Cold, "Still Hands", "+10% critical chance.", false, new[] { "CritChance+0.10" }),
        new("frost_marrow", House.Cold, "Frost Marrow", "+25 frost resistance.", false, new[] { "ResistFrost+25" }),
        new("slow_blood", House.Cold, "Slow Blood", "Bleed and Poison on you last half as long.", false, None, FortuneHook.DotOnSelfHalf),
        new("cold_crown", House.Cold, "Cold Crown", "Shatter leaps to one more Frosted foe within 4 m.", true, None, FortuneHook.ShatterChain),
        // the Toll
        new("toll_road", House.Toll, "Toll Road", "+30% gold found.", false, None, FortuneHook.GoldFound),
        new("ferryman", House.Toll, "Pennies for the Ferryman", "A member who would die keeps 1 HP, once per floor.", false, None, FortuneHook.DeathWard),
        new("lucky_pennies", House.Toll, "Lucky Pennies", "+3 Luck.", false, new[] { "Luck+3" }),
        new("keen_eye", House.Toll, "Keen Eye", "+15 Perception.", false, new[] { "Perception+15" }),
        new("light_fingers", House.Toll, "Light Fingers", "Lockpicking +25% chance.", false, None, FortuneHook.LockBonus),
        new("toll_crown", House.Toll, "Toll Crown", "Elites pay twice: their XP again and a purse of coins when they fall.", true, None, FortuneHook.EliteRewards),
        // the Stone
        new("stone_lungs", House.Stone, "Stone Lungs", "Gas is half as thick to you.", false, None, FortuneHook.GasThin),
        new("second_skin", House.Stone, "Second Skin", "The first hit on each floor is negated.", false, None, FortuneHook.NegateFirstHit),
        new("hungry_edge", House.Stone, "Hungry Edge", "Kills restore 6 stamina.", false, None, FortuneHook.None, TriggerKind.KillStamina, 6f),
        new("thick_hide", House.Stone, "Thick Hide", "+12 armour.", false, new[] { "Armor+12" }),
        new("deep_breath", House.Stone, "Deep Breath", "+15 max stamina, +20% stamina regen.", false, new[] { "MaxStamina+15", "StaminaRegen*1.20" }),
        new("stone_crown", House.Stone, "Stone Crown", "A perfect block or a parry heals 10.", true, None, FortuneHook.BlockHeal),
    };

    private static readonly Dictionary<string, BoonDef> ById = All.ToDictionary(b => b.Id);

    public static BoonDef Get(string id) => ById.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException($"No boon '{id}'");
    public static bool TryGet(string id, out BoonDef def) => ById.TryGetValue(id, out def!);
    public static IEnumerable<BoonDef> OfHouse(House h) => All.Where(b => b.House == h);
    public static BoonDef Crown(House h) => All.First(b => b.House == h && b.Crown);
    /// <summary>How many of a house the party holds (crown included).</summary>
    public static int Held(IEnumerable<string> ids, House h) => ids.Count(id => ById.TryGetValue(id, out var b) && b.House == h);
    public static bool Has(AdventureDto? adv, string id) => adv != null && adv.Boons.Contains(id);
    public static IEnumerable<StatModifier> Modifiers(IEnumerable<string> ids)
        => ids.Where(ById.ContainsKey).Select(Get).SelectMany(def => StatModifierParser.ParseAll(def.Modifiers, def.Name));
    /// <summary>The sum of the held boons' magnitudes for a trigger kind (Hungry Edge's 6 stamina).</summary>
    public static float TriggerMagnitude(AdventureDto? adv, TriggerKind kind)
        => adv == null ? 0f : adv.Boons.Where(ById.ContainsKey).Select(Get).Where(b => b.Trigger == kind).Sum(b => b.TriggerMagnitude);
}
