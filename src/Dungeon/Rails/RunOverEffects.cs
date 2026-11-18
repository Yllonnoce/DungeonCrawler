using DungeonCrawler.Adventure;
using DungeonCrawler.Combat;
using DungeonCrawler.Enemies;
using DungeonCrawler.Party;
using Godot;

namespace DungeonCrawler.Dungeon.Rails;

/// <summary>
/// What a cart does to the bodies <see cref="RunOverSweep"/> found under its front: each takes the run-over outcome by
/// its kind and the lethality, and a big enemy is also knocked aside. The cart that hit them (the party's, a slag cart)
/// applies the returned plating damage to itself and does its own shaking. Server only. No allocation.
/// </summary>
public static class RunOverEffects
{
    /// <summary>
    /// Apply <paramref name="hits"/> in order: a hero (<see cref="VictimKind.Member"/>), an NPC and an enemy each take
    /// <see cref="RunOverRules.Outcome"/> from <paramref name="sourceName"/>; a big enemy (<see cref="RunOverRules.IsBig"/>)
    /// still standing is moved to <paramref name="setAsideAt"/>(where it was hit). <paramref name="onHit"/> hears each body
    /// once its outcome is applied. Returns the plating damage to the cart: <see cref="RunOverRules.BigBodyPlatingDamage"/>
    /// when any big enemy was hit (once, however many), else 0.
    /// </summary>
    public static float Apply(List<(Node3D body, TrackBody tb)> hits, string sourceName, float lethality, Func<Vector3, Vector3> setAsideAt, Action<Node3D>? onHit = null)
    {
        var hitBig = false;
        foreach (var (body, _) in hits)
        {
            switch (body)
            {
                case Adventurer hero:
                    hero.ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(VictimKind.Member, false, lethality), sourceName, RailController.HazardIndex);
                    break;
                case Enemy enemy:
                {
                    var pos = enemy.GlobalPosition;
                    var big = RunOverRules.IsBig(enemy.Profile.Scale, enemy.IsBoss);
                    enemy.ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(enemy.HazardVictimKind, big, lethality), sourceName, RailController.HazardIndex);
                    if (big)
                    {
                        if (enemy.IsAlive) enemy.GlobalPosition = setAsideAt(pos);
                        hitBig = true;
                    }
                    break;
                }
                case Npc npc:
                    npc.ReceiveHazard(HazardDamage.RunOver, RunOverRules.Outcome(npc.HazardVictimKind, false, lethality), sourceName, RailController.HazardIndex);
                    break;
            }
            onHit?.Invoke(body);
        }
        return hitBig ? RunOverRules.BigBodyPlatingDamage : 0f;
    }
}
