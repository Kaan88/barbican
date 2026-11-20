using System.Text.Json.Nodes;
using Barbican.Core.Compose;
using Barbican.Core.Jmap;

namespace Barbican.Core.Tests.Compose;

public sealed class RawSubmissionReadbackTests
{
    [Fact]
    public void ParsesOnlyExplicitRefusalsAndObservedServerTime()
    {
        var submission = JsonNode.Parse("""{"sendAt":"2030-01-02T03:04:05Z","deliveryStatus":{"bad@example.org":{"delivered":"no","smtpReply":" 550 refused "},"ok@example.org":{"delivered":"queued"}}}""")!.AsObject();
        var result = RawSubmissionReadback.Parse(submission, ["bad@example.org", "ok@example.org"]);
        Assert.Equal(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero), result.SendAt);
        var rejected = Assert.Single(result.RejectedRecipients);
        Assert.Equal("bad@example.org", rejected.Email, StringComparer.Ordinal);
        Assert.Equal("550 refused", rejected.SmtpReply, StringComparer.Ordinal);
        Assert.False(result.AllRefused);
    }

    [Fact]
    public void AllRefusedRequiresCompleteExplicitRecipients()
    {
        var submission = JsonNode.Parse("""{"deliveryStatus":{"bad@example.org":{"delivered":"no"}}}""")!.AsObject();
        var complete = RawSubmissionReadback.Parse(submission, ["bad@EXAMPLE.ORG"]);
        Assert.True(complete.AllRefused);
        Assert.Equal(string.Empty, Assert.Single(complete.RejectedRecipients).SmtpReply, StringComparer.Ordinal);
        Assert.False(RawSubmissionReadback.Parse(submission, ["bad@example.org", "missing@example.org"]).AllRefused);
        Assert.False(RawSubmissionReadback.Parse(new JsonObject(), []).AllRefused);
    }
    [Fact]
    public void UnexpectedMalformedAndDifferentLocalPartCannotProveRefusal()
    {
        var extra = JsonNode.Parse("""{"deliveryStatus":{"bad@example.org":{"delivered":"no"},"other@example.org":{"delivered":"no"}}}""")!.AsObject();
        var result = RawSubmissionReadback.Parse(extra, ["bad@example.org"]);
        Assert.Empty(result.RejectedRecipients);
        Assert.False(result.AllRefused);
        var changedCase = JsonNode.Parse("""{"deliveryStatus":{"BAD@example.org":{"delivered":"no"}}}""")!.AsObject();
        Assert.Empty(RawSubmissionReadback.Parse(changedCase, ["bad@example.org"]).RejectedRecipients);
        var invalidReply = JsonNode.Parse("""{"deliveryStatus":{"bad@example.org":{"delivered":"no","smtpReply":"bad\nreply"}}}""")!.AsObject();
        Assert.False(RawSubmissionReadback.Parse(invalidReply, ["bad@example.org"]).AllRefused);
        Assert.Empty(RawSubmissionReadback.Parse(invalidReply, ["bad@example.org"]).RejectedRecipients);
        Assert.Empty(RawSubmissionReadback.Parse(changedCase, []).RejectedRecipients);
    }

    [Fact]
    public void DomainIdnNormalizationKeepsLocalPartCase()
    {
        var submission = JsonNode.Parse("""{"deliveryStatus":{"Name@xn--bcher-kva.example":{"delivered":"no"}}}""")!.AsObject();
        Assert.True(RawSubmissionReadback.Parse(submission, ["Name@bücher.example"]).AllRefused);
        Assert.False(RawSubmissionReadback.Parse(submission, ["name@bücher.example"]).AllRefused);
    }
    [Fact]
    public void ContradictoryNotFoundRejectsReadback()
    {
        static JmapResponse Response(string notFound) => new() { MethodResponses =
            [JsonNode.Parse("""["EmailSubmission/get",{"accountId":"a","list":[{"id":"s"}],"notFound":NOTFOUND},"raw-readback"]""".Replace("NOTFOUND", notFound, StringComparison.Ordinal))!.AsArray()] };
        Assert.Null(RawSubmissionReadback.ParseResponse(Response("[\"s\"]"), "a", "s", ["a@example.org"]));
        Assert.Null(RawSubmissionReadback.ParseResponse(Response("null"), "a", "s", ["a@example.org"]));
        Assert.NotNull(RawSubmissionReadback.ParseResponse(Response("[]"), "a", "s", ["a@example.org"]));
    }

    [Fact]
    public void MissingStatusPreservesCreationRefusalButMalformedOrContradictoryStatusCannotCleanUp()
    {
        var initial = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"a@example.org":{"delivered":"no"}}}""")!.AsObject(), ["a@example.org"]);
        var timeOnly = RawSubmissionReadback.Parse(JsonNode.Parse("""{"sendAt":"2030-01-02T03:04:05Z"}""")!.AsObject(), ["a@example.org"]);
        var merged = RawSubmissionReadback.Merge(initial, timeOnly)!;
        Assert.True(merged.AllRefused);
        Assert.Single(merged.RejectedRecipients);
        Assert.NotNull(merged.SendAt);
        var invalid = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"a@example.org":5}}""")!.AsObject(), ["a@example.org"]);
        Assert.False(RawSubmissionReadback.Merge(initial, invalid)!.AllRefused);
        var contradictory = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"a@example.org":{"delivered":"yes"}}}""")!.AsObject(), ["a@example.org"]);
        Assert.False(RawSubmissionReadback.Merge(initial, contradictory)!.AllRefused);
        Assert.False(RawSubmissionReadback.Merge(contradictory, initial)!.AllRefused);
    }
    [Fact]
    public void QueuedOrMissingStatusesCanBecomeTerminalRefusals()
    {
        string[] recipients = ["a@example.org", "b@example.org"];
        var initial = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"a@example.org":{"delivered":"queued"}}}""")!.AsObject(), recipients);
        var observed = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"a@example.org":{"delivered":"no"},"b@example.org":{"delivered":"no"}}}""")!.AsObject(), recipients);
        var merged = RawSubmissionReadback.Merge(initial, observed)!;
        Assert.True(merged.AllRefused);
        Assert.Equal(2, merged.RejectedRecipients.Count);
        var missing = RawSubmissionReadback.Parse(new JsonObject(), recipients);
        Assert.True(RawSubmissionReadback.Merge(missing, observed)!.AllRefused);
        var partial = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"a@example.org":{"delivered":"no"}}}""")!.AsObject(), recipients);
        var rest = RawSubmissionReadback.Parse(JsonNode.Parse("""{"deliveryStatus":{"b@example.org":{"delivered":"no"}}}""")!.AsObject(), recipients);
        Assert.True(RawSubmissionReadback.Merge(partial, rest)!.AllRefused);
    }
}
