// Mirrors: RFC 8621 §4.1.1 (keywords: a String[Boolean] map, every value true); Rust jmap.rs:1627 (an invalid keyword is skipped).
// Owner jmap.

using System.Text.Json;
using System.Text.Json.Serialization;
using Barbican.Core.Jmap.KeywordSyntax;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Jmap.Json;

/// <summary>Reads and writes <see cref="Keywords"/> as the JMAP keyword map.</summary>
public sealed class KeywordsConverter : JsonConverter<Keywords>
{
    /// <inheritdoc/>
    public override Keywords Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Keywords.None;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return Keywords.None;
        }

        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var key = reader.GetString()!;
            reader.Read();
            var on = reader.TokenType == JsonTokenType.True;
            reader.Skip();
            if (on && KeywordRules.IsValid(key))
            {
                map[key] = true;
            }
        }

        return new Keywords(map);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Keywords value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        foreach (var (k, on) in value.Map)
        {
            if (on)
            {
                writer.WriteBoolean(k, true);
            }
        }

        writer.WriteEndObject();
    }
}
