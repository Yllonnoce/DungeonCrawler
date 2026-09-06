using DungeonCrawler.Characters;
using DungeonCrawler.Combat;
using DungeonCrawler.Tests.Characters;
using Xunit;

namespace DungeonCrawler.Tests.Combat;

public class HitResolverTests
{
    private static readonly AttackProfile Sword = new() { MinDamage = 10, MaxDamage = 10 };

    [Fact]
    public void Mitigate_ArmorAndResist()
    {
        Assert.Equal(10f, DamagePipeline.Mitigate(10, DamageType.Physical, TestClasses.Dummy()), 3);
        Assert.Equal(5f, DamagePipeline.Mitigate(10, DamageType.Physical, TestClasses.Dummy(armor: 100)), 3);
        Assert.Equal(5f, DamagePipeline.Mitigate(10, DamageType.Fire, TestClasses.Dummy(resistAll: 50)), 3);
        Assert.Equal(2.5f, DamagePipeline.Mitigate(10, DamageType.Fire, TestClasses.Dummy(resistAll: 500)), 3); // capped at 75
    }

    [Fact]
    public void Resolve_HitChanceUsesAccuracyVsEvasion()
    {
        Assert.Equal(0.98f, DamagePipeline.HitChance(TestClasses.Attacker(accuracy: 200), TestClasses.Dummy()), 3);
        Assert.Equal(0.3f, DamagePipeline.HitChance(TestClasses.Attacker(accuracy: 0), TestClasses.Dummy(evasion: 200)), 3);
        var rng = new Random(1);
        var hits = 0;
        for (var i = 0; i < 2000; i++)
            if (HitResolver.Resolve(TestClasses.Attacker(accuracy: 75), TestClasses.Dummy(evasion: 50), null, Sword, HitContext.None, rng).Hit) hits++;
        Assert.InRange(hits / 2000.0, 0.95, 1.0);
    }

    [Fact]
    public void Resolve_HeavyCritSneakMultiply()
    {
        var rng = new Random(3);
        var plain = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), null, Sword, HitContext.None, rng);
        Assert.True(plain.Hit);
        Assert.Equal(10f, plain.Amount, 3);

        var heavy = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), null, Sword, new HitContext { Heavy = true, HeavyMultiplier = 2f }, rng);
        Assert.Equal(20f, heavy.Amount, 3);

        var sneak = HitResolver.Resolve(TestClasses.Attacker(accuracy: 0, critMult: 2f), TestClasses.Dummy(evasion: 999), null, Sword, new HitContext { SneakAttack = true, SneakMultiplier = 3f }, rng);
        Assert.True(sneak.Hit);   // sneak attacks never miss
        Assert.True(sneak.Crit);  // and always crit
        Assert.Equal(60f, sneak.Amount, 3);
    }

    [Fact]
    public void Resolve_BlockReducesAndSpellsIgnoreBlock()
    {
        var rng = new Random(5);
        var blocked = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), null, Sword, new HitContext { DefenderBlocking = true }, rng);
        Assert.True(blocked.Blocked);
        Assert.Equal(2.5f, blocked.Amount, 3);

        var spell = Sword with { Kind = AttackKind.Spell, Type = DamageType.Fire };
        var notBlocked = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), null, spell, new HitContext { DefenderBlocking = true }, rng);
        Assert.False(notBlocked.Blocked);
        Assert.Equal(10f, notBlocked.Amount, 3);
    }

    [Fact]
    public void Resolve_ShieldAbsorbsFirst()
    {
        var rng = new Random(7);
        var effects = new StatusEffectController();
        effects.Apply(new StatusApplication(StatusKind.Shield, 10, 6));
        var r = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), effects, Sword, HitContext.None, rng);
        Assert.Equal(4f, r.Amount, 3);
        Assert.Equal(0f, effects.ShieldPoints);
    }

    [Fact]
    public void Resolve_AppliesOnHitStatuses()
    {
        var rng = new Random(11);
        var effects = new StatusEffectController();
        var poisonSword = Sword with { OnHit = new[] { new StatusApplication(StatusKind.Poison, 6, 2, 1f) } };
        var r = HitResolver.Resolve(TestClasses.Attacker(accuracy: 500), TestClasses.Dummy(), effects, poisonSword, HitContext.None, rng);
        Assert.Single(r.Applied);
        Assert.True(effects.Has(StatusKind.Poison));
    }

    [Fact]
    public void Resolve_StunnedDefenderAlwaysHit()
    {
        var rng = new Random(13);
        for (var i = 0; i < 50; i++)
            Assert.True(HitResolver.Resolve(TestClasses.Attacker(accuracy: 0), TestClasses.Dummy(evasion: 999), null, Sword, new HitContext { DefenderStunned = true }, rng).Hit);
    }
}
