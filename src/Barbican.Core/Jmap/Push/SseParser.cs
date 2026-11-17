// Mirrors: Rust jmap.rs:1583 SseParser; WHATWG HTML "server-sent events" §9.2.6 (field parsing: `event`, multi-line `data`, `:`
// comments, CR LF line ends); RFC 8620 §7.3 (EventSource: `state` events carry a StateChange, `ping` keep-alives); Stalwart
// crates/jmap/src/api/event_source.rs (`event: ping`, `event: calendarAlert`). Owner jmap.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Push;

/// <summary>Line-by-line text/event-stream parser.</summary>
public sealed class SseParser
{
    private readonly StringBuilder _data = new();
    private string _event = "";

    /// <summary>Feeds one line (with or without a trailing CR); returns an event when a blank line completes one.</summary>
    public PushEvent? Line(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line.EndsWith('\r') ? line[..^1] : line;
        if (text.Length == 0)
        {
            return Dispatch();
        }

        if (text[0] == ':')
        {
            return null;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var field = colon < 0 ? text : text[..colon];
        var value = colon < 0 ? "" : text[(colon + 1)..];
        if (value.StartsWith(' '))
        {
            value = value[1..];
        }

        switch (field)
        {
            case "event":
                _event = value;
                break;
            case "data":
                if (_data.Length > 0)
                {
                    _data.Append('\n');
                }

                _data.Append(value);
                break;
            default:
                break;
        }

        return null;
    }

    private PushEvent? Dispatch()
    {
        var name = _event;
        var data = _data.ToString();
        _event = "";
        _data.Clear();
        if (name.Length == 0 && data.Length == 0)
        {
            return null;
        }

        var json = Parse(data);
        return name switch
        {
            "ping" => new PushPing(),
            "state" => new PushState(json ?? new JsonObject()),
            "" when string.Equals(json.Str("@type"), "StateChange", StringComparison.Ordinal) => new PushState(json!),
            _ => new PushOther(name.Length == 0 ? "message" : name, json),
        };
    }

    private static JsonNode? Parse(string data)
    {
        if (data.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
