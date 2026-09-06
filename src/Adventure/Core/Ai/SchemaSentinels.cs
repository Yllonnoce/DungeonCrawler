using System.Collections;
using System.Reflection;

namespace DungeonCrawler.Adventure.Ai;

/// <summary>
/// Undoes the schema's "no nulls" rule after a model reply: properties marked <see cref="AiNotNullAttribute"/> that hold
/// "" (strings), a negative number, or an object with an empty id/name become null again. Walks lists and nested plans.
/// </summary>
public static class SchemaSentinels
{
    public static T Normalize<T>(T value) where T : class
    {
        Walk(value, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return value;
    }

    private static void Walk(object? node, HashSet<object> seen)
    {
        if (node == null || node is string || !seen.Add(node)) return;
        if (node is IEnumerable list && node is not IDictionary)
        {
            foreach (var item in list) Walk(item, seen);
            return;
        }
        var type = node.GetType();
        if (!type.IsClass || type.Namespace?.StartsWith("DungeonCrawler") != true) return;
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
            var value = p.GetValue(node);
            if (p.CanWrite && p.GetCustomAttribute<AiNotNullAttribute>() != null && IsNone(value)) { p.SetValue(node, null); continue; }
            Walk(value, seen);
        }
    }

    private static bool IsNone(object? value) => value switch
    {
        null => true,
        string s => s.Trim().Length == 0,
        int i => i < 0,
        float f => f < 0f,
        double d => d < 0,
        BetrayalPlan b => string.IsNullOrWhiteSpace(b.EnemyId),
        ObjectivePlan o => string.IsNullOrWhiteSpace(o.Ref),
        BossPlan boss => string.IsNullOrWhiteSpace(boss.Name) && string.IsNullOrWhiteSpace(boss.IntroLine.Text) && string.IsNullOrWhiteSpace(boss.DeathLine.Text),
        _ => false,
    };
}
