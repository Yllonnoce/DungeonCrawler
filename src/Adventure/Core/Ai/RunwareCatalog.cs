namespace DungeonCrawler.Adventure.Ai;

/// <summary>One text model Runware routes, with its price per million tokens. <see cref="Air"/> is the id the task API takes.</summary>
public sealed record RunwareTextModel(string Slug, string Name, double InputPerMillion, double OutputPerMillion)
{
    public string Air => RunwareWire.ToAir(Slug);
    public bool Priced => InputPerMillion > 0 || OutputPerMillion > 0;
    /// <summary>"Claude Sonnet 4.6 ($3.00 in / $15.00 out per 1M)" for a menu.</summary>
    public string Label => Priced ? $"{Name} (${InputPerMillion:0.00} in / ${OutputPerMillion:0.00} out per 1M)" : $"{Name} (price not listed)";

    /// <summary>Dearest first by output price, then input, then name; unpriced models last.</summary>
    public static List<RunwareTextModel> DearestFirst(IEnumerable<RunwareTextModel> models)
        => models.OrderByDescending(m => m.Priced).ThenByDescending(m => m.OutputPerMillion).ThenByDescending(m => m.InputPerMillion).ThenBy(m => m.Name).ToList();
}

/// <summary>
/// What Runware listed on 2026-09-07, for the menus when there is no key or the list cannot be fetched; a fetch with a
/// key replaces it. Prices are per million tokens.
/// </summary>
public static class RunwareCatalog
{
    /// <summary>The list last fetched from Runware with a key (the Settings screen sets it); null until then.</summary>
    public static IReadOnlyList<RunwareTextModel>? Live { get; set; }

    /// <summary>The list to show and price from: the fetched one when there is one, else the shipped one.</summary>
    public static IReadOnlyList<RunwareTextModel> Current => Live is { Count: > 0 } live ? live : TextModels;

    /// <summary>List prices per million tokens for a model (slug or AIR), or null when it is not in the catalogue.</summary>
    public static (double InputPerMillion, double OutputPerMillion)? PriceFor(string model)
    {
        var air = RunwareWire.ToAir(model);
        var m = Current.FirstOrDefault(m => m.Air.Equals(air, StringComparison.OrdinalIgnoreCase) || m.Slug.Equals(model.Trim(), StringComparison.OrdinalIgnoreCase));
        return m is { Priced: true } ? (m.InputPerMillion, m.OutputPerMillion) : null;
    }

    public static readonly IReadOnlyList<RunwareTextModel> TextModels = RunwareTextModel.DearestFirst(new RunwareTextModel[]
    {
        new("openai-gpt-5-4-pro", "OpenAI GPT-5.4 Pro", 30, 180),
        new("anthropic-claude-fable-5", "Claude Fable 5", 10, 50),
        new("openai-gpt-5-5", "OpenAI GPT-5.5", 5, 30),
        new("anthropic-claude-opus-4-8", "Claude Opus 4.8", 5, 25),
        new("anthropic-claude-opus-4-7", "Claude Opus 4.7", 5, 25),
        new("openai-gpt-5-6-sol", "OpenAI GPT-5.6 Sol", 4, 20),
        new("anthropic-claude-sonnet-4-6", "Claude Sonnet 4.6", 3, 15),
        new("moonshotai-kimi-k3", "Moonshot Kimi K3", 3, 15),
        new("openai-gpt-5-4", "OpenAI GPT-5.4", 2.5, 15),
        new("google-gemini-3-1-pro", "Google Gemini 3.1 Pro", 2, 12),
        new("openai-gpt-5-6-terra", "OpenAI GPT-5.6 Terra", 2, 12),
        new("openai-gpt-5-1", "OpenAI GPT-5.1", 1.25, 10),
        new("google-gemini-3-5-flash", "Google Gemini 3.5 Flash", 1.5, 9),
        new("google-gemini-3-6-flash", "Google Gemini 3.6 Flash", 1.5, 7.5),
        new("anthropic-claude-haiku-4-5", "Claude Haiku 4.5", 1, 5),
        new("openai-gpt-5-4-mini", "OpenAI GPT-5.4 Mini", 0.75, 4.5),
        new("zai-glm-5-1", "Z.ai GLM-5.1", 1.4, 4.4),
        new("zai-glm-5-3", "Z.ai GLM-5.3", 1.2, 4),
        new("google-gemini-3-7-flash", "Google Gemini 3.7 Flash", 0.75, 3.75),
        new("moonshotai-kimi-k2-6", "Moonshot Kimi K2.6", 0.6, 3.05),
        new("google-gemini-3-flash", "Google Gemini 3 Flash", 0.5, 3),
        new("zai-glm-5-2", "Z.ai GLM-5.2", 0.8, 2.55),
        new("xai-grok-4-3", "xAI Grok 4.3", 1.25, 2.5),
        new("google-gemini-3-5-flash-lite", "Google Gemini 3.5 Flash Lite", 0.3, 2.5),
        new("minimax-m2-7-highspeed", "MiniMax M2.7 Highspeed", 0.6, 2.4),
        new("openai-gpt-5-mini", "OpenAI GPT-5 Mini", 0.25, 2),
        new("deepseek-v4-pro", "DeepSeek V4 Pro", 0.96, 1.92),
        new("google-gemini-3-1-flash-lite", "Google Gemini 3.1 Flash Lite", 0.25, 1.5),
        new("openai-gpt-5-4-nano", "OpenAI GPT-5.4 Nano", 0.2, 1.25),
        new("minimax-m3", "MiniMax M3", 0.3, 1.2),
        new("minimax-m2-7", "MiniMax M2.7", 0.3, 1.2),
        new("openai-gpt-5-6-luna", "OpenAI GPT-5.6 Luna", 0.2, 1.2),
        new("openai-gpt-5-nano", "OpenAI GPT-5 Nano", 0.05, 0.4),
        new("google-gemma-4-31b", "Google Gemma 4 31B", 0.1, 0.3),
        new("zai-glm-5-3-flash", "Z.ai GLM-5.3 Flash", 0.07, 0.25),
        new("deepseek-v4-flash", "DeepSeek V4 Flash", 0.08, 0.15),
        new("openai-gpt-oss-120b", "OpenAI GPT-OSS 120B", 0.03, 0.14),
        new("qwen3.5-9b", "Qwen 3.5 9B", 0.09, 0.13),
        new("qwen3.5-4b", "Qwen 3.5 4B", 0.05, 0.07),
    });
}
