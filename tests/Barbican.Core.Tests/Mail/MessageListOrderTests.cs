// Bulwark message-list-order and keyword-polarity compatibility; verifies real request shapes and fallback behavior.
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Contacts;

namespace Barbican.Core.Tests.Mail;

public sealed class MessageListOrderTests
{
    [Theory]
    [InlineData("unread_first", "$seen", true)]
    [InlineData("starred_first", "$flagged", false)]
    [InlineData("tagged_first", "$label:work", false)]
    public void Presets_have_RFC_polarity_and_a_date_tie_breaker(string preset, string keyword, bool ascending)
    {
        var sort = MessageListOrder.Build(MessageListOrder.Preset(preset, "work"));
        Assert.Equal(new SortComparator("hasKeyword", ascending, keyword), sort[0]);
        Assert.Equal(new SortComparator("receivedAt", false), sort[1]);
    }

    [Fact]
    public void Imported_levels_drop_invalid_duplicate_and_excess_entries()
    {
        var value = JsonNode.Parse("""[{"criterion":"unknown"},{"criterion":"tag"},{"criterion":"subject"},{"criterion":"subject","direction":"desc"},{"criterion":"tag","tagId":"work"},{"criterion":"size","direction":"asc"},{"criterion":"sentAt"}]""");
        var levels = MessageListOrder.Parse(value);
        Assert.Equal([new("subject", "asc"), new("tag", "desc", "work"), new MessageSortLevel("size", "asc")], levels);
        Assert.Equal(levels, MessageListOrder.Parse(MessageListOrder.ToJson(levels)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Paging_and_delta_use_the_same_probed_sort_and_get_rows_follow_query_ids(bool inverted)
    {
        var client = new ContactScriptedClient(request => Answer(request, inverted, unsupported: false), [JmapCapabilities.Mail]);
        var semantic = MessageListOrder.Build(MessageListOrder.Preset("unread_first"));
        var page = await ListPageFetcher.FetchAsync(client, [], null, 2, TestContext.Current.CancellationToken, sort: semantic);
        Assert.True(page.IsOk);
        Assert.Equal(["b", "a"], page.Value!.Items.Select(item => item.Id), StringComparer.Ordinal);
        await ListPageFetcher.FetchAsync(client, [], new ListCursor("a") { Position = 2 }, 2, TestContext.Current.CancellationToken, sort: semantic);
        var resolved = await ListSortResolver.ResolveAsync(client, semantic, TestContext.Current.CancellationToken);
        var delta = ListDeltaFetcher.Build("acc", [], new ListStates("q", "e", "t"), 50, resolved, collapseThreads: true).Request;
        var queries = client.Requests.Skip(1).Select(request => request.MethodCalls[0][1]!["sort"]!.ToJsonString()).ToList();
        Assert.Equal(2, queries.Count);
        Assert.All(queries, sort => Assert.Equal(queries[0], sort));
        Assert.Equal(queries[0], delta.MethodCalls[1][1]!["sort"]!.ToJsonString());
        Assert.Equal(!inverted, (bool?)delta.MethodCalls[1][1]!["sort"]![0]!["isAscending"]);
        Assert.True((bool?)delta.MethodCalls[1][1]!["collapseThreads"]);
        Assert.Equal(3, client.Requests.Count); // one probe for both pages and the delta
    }

    [Fact]
    public async Task Unsupported_keyword_sort_falls_back_with_a_visible_warning()
    {
        var client = new ContactScriptedClient(request => Answer(request, inverted: false, unsupported: true), [JmapCapabilities.Mail]);
        var page = await ListPageFetcher.FetchAsync(client, [], null, 2, TestContext.Current.CancellationToken, sort: MessageListOrder.Build(MessageListOrder.Preset("starred_first")));
        Assert.True(page.IsOk);
        Assert.NotNull(page.Value!.Warning);
        Assert.Equal("receivedAt", (string?)client.Requests[^1].MethodCalls[0][1]!["sort"]![0]!["property"]);
    }

    [Fact]
    public void Unified_semantic_comparison_prioritizes_keywords_and_normalizes_subjects()
    {
        var oldStar = new EmailSummary { Id = "old", Keywords = Keywords.Of("$flagged"), ReceivedAt = DateTimeOffset.UnixEpoch };
        var newer = new EmailSummary { Id = "new", ReceivedAt = DateTimeOffset.UnixEpoch.AddDays(1) };
        Assert.True(MessageListOrder.Compare(oldStar, newer, MessageListOrder.Build(MessageListOrder.Preset("starred_first"))) < 0);
        var subject = MessageListOrder.Build([new("subject", "asc")]);
        Assert.True(MessageListOrder.Compare(oldStar with { Subject = "Re: Apples" }, newer with { Subject = "Bananas" }, subject) < 0);
        Assert.True(MessageListOrder.Compare(oldStar with { Subject = null }, newer with { Subject = "Bananas" }, subject) > 0);
    }

    private static JmapResponse Answer(JmapRequest request, bool inverted, bool unsupported)
    {
        var probe = request.MethodCalls.Count == 3 && string.Equals((string?)request.MethodCalls[1][0], "Email/query", StringComparison.Ordinal);
        var responses = new List<JsonArray>();
        for (var index = 0; index < request.MethodCalls.Count; index++)
        {
            var call = request.MethodCalls[index];
            var method = (string)call[0]!;
            JsonObject args;
            if (probe && unsupported)
            {
                responses.Add(new JsonArray("error", new JsonObject { ["type"] = "unsupportedSort" }, (string)call[2]!));
                continue;
            }

            if (probe)
            {
                var ascending = inverted ? "seen" : "unread";
                args = index switch
                {
                    0 => new JsonObject { ["ids"] = new JsonArray(ascending) },
                    1 => new JsonObject { ["ids"] = new JsonArray(inverted ? "unread" : "seen") },
                    _ => new JsonObject { ["list"] = new JsonArray(new JsonObject { ["id"] = ascending, ["keywords"] = inverted ? new JsonObject { ["$seen"] = true } : new JsonObject() }) },
                };
            }
            else args = method switch
            {
                "Email/query" => new JsonObject { ["ids"] = new JsonArray("b", "a"), ["total"] = 4, ["queryState"] = "q", ["canCalculateChanges"] = true },
                "Email/get" => new JsonObject { ["list"] = new JsonArray(new JsonObject { ["id"] = "a" }, new JsonObject { ["id"] = "b" }), ["state"] = "e" },
                _ => new JsonObject { ["list"] = new JsonArray(), ["state"] = "t" },
            };
            responses.Add(new JsonArray(method, args, (string)call[2]!));
        }

        return new JmapResponse { MethodResponses = responses };
    }
}
