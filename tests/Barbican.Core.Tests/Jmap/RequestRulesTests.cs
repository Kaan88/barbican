// Ported from Rust src/jmap.rs tests calls_split_by_limit_unless_linked (:1812), read_only_calls (:1826), problem_details (:1832).
//

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Tests.Jmap;

public sealed class RequestRulesTests
{
    private static JsonNode Call(string method) => JsonNode.Parse($$"""["{{method}}", {"accountId": "a"}, "x"]""")!;

    private static List<JsonNode> FiveQueries() => [.. Enumerable.Range(0, 5).Select(_ => Call("Email/query"))];

    [Fact]
    [RustTest("jmap::calls_split_by_limit_unless_linked")]
    public void Calls_split_by_limit_unless_linked()
    {
        var calls = FiveQueries();
        Assert.Equal([2, 2, 1], RequestSplitter.SplitCalls(calls, 2).Select(x => x.Count));
        Assert.Single(RequestSplitter.SplitCalls(calls, 16));

        var linked = FiveQueries();
        linked.Add(JsonNode.Parse("""["Email/get", {"#ids": {"resultOf": "x", "name": "Email/query", "path": "/ids"}}, "y"]""")!);
        Assert.Single(RequestSplitter.SplitCalls(linked, 2));

        var created = FiveQueries();
        created.Add(JsonNode.Parse("""["EmailSubmission/set", {"create": {"s": {"emailId": "#d"}}}, "s"]""")!);
        Assert.Single(RequestSplitter.SplitCalls(created, 2));
    }

    [Fact]
    [RustTest("jmap::read_only_calls")]
    public void Read_only_calls()
    {
        Assert.True(ReadOnlyGuard.IsReadOnly(JsonNode.Parse("""[["Email/get", {}, "a"], ["Email/queryChanges", {}, "b"]]""")!.AsArray()));
        Assert.False(ReadOnlyGuard.IsReadOnly(JsonNode.Parse("""[["Email/get", {}, "a"], ["Email/set", {}, "b"]]""")!.AsArray()));
    }

    [Fact]
    [RustTest("jmap::problem_details")]
    public void Problem_details()
    {
        var m = ProblemMessages.Message(400, JsonNode.Parse("""{"type": "urn:ietf:params:jmap:error:limit", "limit": "maxCallsInRequest", "status": 400}""")!.AsObject());
        Assert.Contains("maxCallsInRequest", m, StringComparison.Ordinal);
        Assert.Contains("capability", ProblemMessages.Message(400, JsonNode.Parse("""{"type": "urn:ietf:params:jmap:error:unknownCapability"}""")!.AsObject()), StringComparison.Ordinal);
        Assert.Contains("500", ProblemMessages.Message(500, new JsonObject()), StringComparison.Ordinal);
    }
}
