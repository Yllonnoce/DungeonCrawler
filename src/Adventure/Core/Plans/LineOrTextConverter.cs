using System.Text.Json;
using System.Text.Json.Serialization;

namespace DungeonCrawler.Adventure;

/// <summary>
/// A <see cref="Line"/> that older plans wrote as a bare string (a rail encounter's line in plans 2–4): a string reads as
/// an unvoiced line, an object as a line, null as null. It always writes the object form, so a save written now keeps
/// the line's voice id.
/// </summary>
public sealed class LineOrTextConverter : JsonConverter<Line?>
{
    public override Line? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Null => null,
        JsonTokenType.String => new Line(reader.GetString() ?? ""),
        _ => JsonSerializer.Deserialize<Line>(ref reader, options),
    };

    public override void Write(Utf8JsonWriter writer, Line? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else JsonSerializer.Serialize(writer, value, options);
    }
}
