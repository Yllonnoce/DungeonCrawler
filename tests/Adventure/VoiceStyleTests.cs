using DungeonCrawler.Adventure;
using DungeonCrawler.Adventure.Voice;
using Xunit;

namespace DungeonCrawler.Tests.Adventure;

public class VoiceStyleTests
{
    [Fact]
    public void NobodyWhispersByDefault()
    {
        foreach (var s in new[] { Speaker.Npc, Speaker.Companion, Speaker.Villain, Speaker.Boss })
            Assert.DoesNotContain("hisper", Voices.Style(s));
        Assert.Contains("mock", Voices.Style(Speaker.Villain), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Boom", Voices.Style(Speaker.Boss));
        Assert.Contains("Narrate", Voices.Style(Speaker.Narrator));
    }

    [Fact]
    public void Style_ComposesPersonaAndDelivery()
    {
        var persona = new Persona { VoiceStyle = "a gruff old smith, gravel in the voice", DefaultDelivery = "weary" };
        var plain = Voices.Style(Speaker.Npc, persona, null);
        Assert.StartsWith("Speak as a gruff old smith", plain);
        Assert.Contains("tired", plain);
        var whispered = Voices.Style(Speaker.Npc, persona, Delivery.Whisper);
        Assert.Contains("Whisper", whispered);
        Assert.NotEqual(plain, whispered);
        Assert.NotEqual(VoiceLineId.For("hello", "Kore", plain), VoiceLineId.For("hello", "Kore", whispered));
    }

    [Fact]
    public void Deliveries_ParseAndList()
    {
        Assert.True(Deliveries.TryParse("Shout", out var d)); Assert.Equal(Delivery.Shout, d);
        Assert.False(Deliveries.TryParse("default", out _));
        Assert.False(Deliveries.TryParse("loud", out _));
        Assert.Equal(12, Deliveries.All.Length);
        foreach (var x in Deliveries.All) Assert.NotEqual("", Deliveries.Instruction(x));
    }

    [Fact]
    public void Voices_CatalogAndCompanions()
    {
        Assert.True(Voices.IsKnown("kore"));
        Assert.Equal("Kore", Voices.Canonical("KORE"));
        Assert.Null(Voices.Canonical("Bob"));
        Assert.Equal(Delivery.Shout, Voices.BarkDelivery(BarkTrigger.Kill));
        Assert.Equal(Delivery.Cheerful, Voices.BarkDelivery(BarkTrigger.Loot));
        Assert.NotEqual(Voices.CompanionPersona("class.warrior").VoiceStyle, Voices.CompanionPersona("class.rogue").VoiceStyle);
    }

    [Fact]
    public void Queue_RequeuesUnderTheSameId_AndSpotsRefusals()
    {
        var q = new VoiceQueue();
        var id = q.Enqueue("you bastard", "Fenrir", "style", VoicePriority.Boss, _ => false, "m", fallbackText: "you wretch");
        var job = q.Next()!;
        Assert.Equal("you wretch", job.FallbackText);
        q.Forget(id);
        q.Requeue(job with { Text = job.FallbackText!, FallbackText = null });
        var again = q.Next()!;
        Assert.Equal(id, again.Id);
        Assert.Equal("you wretch", again.Text);
        Assert.True(VoiceQueue.LooksLikeRefusal("Request blocked by content policy"));
        Assert.False(VoiceQueue.LooksLikeRefusal("429 rate limited"));
    }
}
