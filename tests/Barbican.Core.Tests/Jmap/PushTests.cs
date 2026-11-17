// Ported from Rust src/jmap.rs test sse_parser_events (:1840). Red until the jmap stream implements SseParser and PushChanges.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Push;

namespace Barbican.Core.Tests.Jmap;

public sealed class PushTests
{
    [Fact]
    [RustTest("jmap::sse_parser_events")]
    [Red("jmap")]
    public void Sse_parser_events()
    {
        var p = new SseParser();
        var events = new List<PushEvent>();
        string[] lines =
        [
            "event: state", "data: {\"@type\":\"StateChange\",", "data: \"changed\":{\"a\":{\"Email\":\"s1\"}}}", string.Empty,
            ": comment", "event: ping", "data: {\"interval\": 30000}", string.Empty, string.Empty, "event: calendarAlert", "data: {}", string.Empty,
            "data: {\"@type\":\"StateChange\",\"changed\":{}}", "\r",
        ];
        foreach (var line in lines)
        {
            var e = p.Line(line);
            if (e is not null)
            {
                events.Add(e);
            }
        }

        Assert.Equal(4, events.Count);
        var state = Assert.IsType<PushState>(events[0]);
        Assert.Equal("s1", PushChanges.ChangedTypes(state.Change, "a")["Email"]);
        Assert.IsType<PushPing>(events[1]);
        Assert.Equal("calendarAlert", Assert.IsType<PushOther>(events[2]).Name);
        Assert.IsType<PushState>(events[3]);
        Assert.Empty(PushChanges.ChangedTypes(JsonNode.Parse("""{"changed": {"b": {"Email": "x"}}}""")!, "a"));
    }
}
