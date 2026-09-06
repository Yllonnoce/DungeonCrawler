using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DungeonCrawler.Adventure.Ai;

/// <summary>Leave this property out of the AI's output schema; the game fills it in.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AiIgnoreAttribute : Attribute { }

/// <summary>Description shown to the model for a property or type.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class)]
public sealed class AiDescriptionAttribute : Attribute
{
    public string Text { get; }
    public AiDescriptionAttribute(string text) => Text = text;
}

/// <summary>
/// The property is nullable in C# but plain in the schema: the model writes "" (strings), -1 (numbers) or an empty
/// object for "none", and <see cref="SchemaSentinels"/> turns those back into null. The API allows only a handful of
/// nullable parameters per schema.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AiNotNullAttribute : Attribute { }

/// <summary>What the outline call returns: the campaign plus the first floor, designed in one go.</summary>
public sealed class OutlineWithFirstFloor
{
    public CampaignPlan Campaign { get; set; } = new();
    public FloorPlan Floor1 { get; set; } = new();
}

/// <summary>The bark bank call returns one set of lines per party member.</summary>
public sealed class BarkBank
{
    public List<BarkSet> Members { get; set; } = new();
}

/// <summary>
/// JSON schemas for structured output, generated from the plan DTOs by reflection so they cannot drift from the
/// classes the parser fills. Every object forbids extra properties and requires all of its own; nullable properties
/// allow null. Recursion is not supported (the DTOs have none).
/// </summary>
public static class PlanSchemas
{
    private static readonly Dictionary<Type, string> Cache = new();
    private static readonly NullabilityInfoContext Nullability = new();

    public static string For<T>() => For(typeof(T));

    public static string For(Type t)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(t, out var cached)) return cached;
            var node = Build(t, new HashSet<Type>(), nullable: false);
            var json = node.ToJsonString();
            Cache[t] = json;
            return json;
        }
    }

    public static JsonObject Build(Type t, HashSet<Type> stack, bool nullable)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying != null) return Build(underlying, stack, nullable); // a Nullable<T> is only a union when the property says so

        JsonObject node;
        if (t == typeof(string)) node = Typed("string", nullable);
        else if (t == typeof(bool)) node = Typed("boolean", nullable);
        else if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(ulong) || t == typeof(uint)) node = Typed("integer", nullable);
        else if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) node = Typed("number", nullable);
        else if (t.IsEnum)
        {
            node = Typed("string", nullable);
            var values = new JsonArray();
            foreach (var name in Enum.GetNames(t)) values.Add(name);
            node["enum"] = values;
        }
        else if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t))
        {
            var item = ElementType(t) ?? throw new NotSupportedException($"Cannot describe collection {t}");
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)) throw new NotSupportedException($"Dictionaries are not allowed in AI output ({t}); use a list of records");
            node = Typed("array", nullable);
            node["items"] = Build(item, stack, nullable: false);
        }
        else if (t.IsClass)
        {
            if (!stack.Add(t)) throw new NotSupportedException($"Recursive type {t} cannot be expressed as a strict schema");
            node = Typed("object", nullable);
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanWrite || p.GetCustomAttribute<AiIgnoreAttribute>() != null) continue;
                var info = Nullability.Create(p);
                var notNull = p.GetCustomAttribute<AiNotNullAttribute>() != null;
                var propNullable = !notNull && info.WriteState == NullabilityState.Nullable;
                var child = Build(p.PropertyType, stack, propNullable);
                var description = p.GetCustomAttribute<AiDescriptionAttribute>()?.Text ?? "";
                if (notNull && info.WriteState == NullabilityState.Nullable)
                {
                    var bare = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                    var sentinel = bare == typeof(string) ? "an empty string" : bare.IsValueType ? "-1" : "an object with empty fields";
                    description = (description == "" ? "" : description + " ") + $"Use {sentinel} for none.";
                }
                if (description != "") child["description"] = description;
                props[p.Name] = child;
                required.Add(p.Name);
            }
            node["properties"] = props;
            node["required"] = required;
            node["additionalProperties"] = false;
            if (t.GetCustomAttribute<AiDescriptionAttribute>() is { } td) node["description"] = td.Text;
            stack.Remove(t);
        }
        else throw new NotSupportedException($"Cannot describe {t}");
        return node;
    }

    private static JsonObject Typed(string type, bool nullable)
        => nullable ? new JsonObject { ["type"] = new JsonArray(type, "null") } : new JsonObject { ["type"] = type };

    private static Type? ElementType(Type t)
    {
        if (t.IsArray) return t.GetElementType();
        foreach (var i in t.GetInterfaces().Prepend(t))
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return i.GetGenericArguments()[0];
        return null;
    }
}
