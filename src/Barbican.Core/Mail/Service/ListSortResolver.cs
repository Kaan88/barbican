// Mirrors: Bulwark lib/jmap/client.ts:2936-3032 (keyword support and polarity probe),
// lib/message-list-order.ts:175-220; RFC 8621 section 4.4.2. Read-only probe, no keyword mutation.
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Service;

/// <summary>Uses one per-session verdict for paging and deltas; older Stalwart versions invert keyword sort polarity.</summary>
public static class ListSortResolver
{
    private static readonly ConditionalWeakTable<IJmapClient, ConcurrentDictionary<string, State>> States = new();

    public static void RefusedKeywords(IJmapClient client, string? accountId = null) => StateOf(client, accountId).Unsupported = true;

    public static bool KeywordsUnsupported(IJmapClient client, string? accountId = null) => StateOf(client, accountId).Unsupported || !AdvertisesKeywords(client, accountId ?? client.Session.MailAccountId);

    public static async Task<IReadOnlyList<SortComparator>?> ResolveAsync(IJmapClient client, IReadOnlyList<SortComparator>? sort, CancellationToken ct, string? accountId = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (sort is null || !sort.Any(item => item.Keyword is not null)) return sort;
        var account = accountId ?? client.Session.MailAccountId;
        var state = StateOf(client, account);
        await state.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(state.Session, client.Session))
            {
                state.Session = client.Session;
                state.Unsupported = false;
                state.Inverted = null;
                state.RetryAfter = DateTimeOffset.MinValue;
            }

            if (AdvertisesKeywords(client, account) && !state.Unsupported && state.Inverted is null
                && TimeProvider.System.GetUtcNow() >= state.RetryAfter && client.Session.Limits.MaxCallsInRequest >= 3)
            {
                state.RetryAfter = TimeProvider.System.GetUtcNow().AddMinutes(1);
                await ProbeAsync(client, state, account, ct).ConfigureAwait(false);
            }

            if (state.Unsupported || !AdvertisesKeywords(client, account))
            {
                var remaining = sort.Where(item => item.Keyword is null).ToList();
                if (remaining.Count == 0) remaining.Add(new("receivedAt", false));
                return remaining;
            }

            return state.Inverted is true ? [.. sort.Select(item => item.Keyword is null ? item : item with { IsAscending = !item.IsAscending })] : sort;
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private static bool AdvertisesKeywords(IJmapClient client, string accountId)
    {
        var cap = client.Session.CapabilityObject(accountId, JmapCapabilities.Mail);
        return cap is not { } value || !value.TryGetProperty("sortOptions", out var options)
            || options.ValueKind != JsonValueKind.Array || options.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "hasKeyword", StringComparison.Ordinal));
    }

    private static async Task ProbeAsync(IJmapClient client, State state, string account, CancellationToken ct)
    {
        var builder = new JmapRequestBuilder();
        var ascending = builder.Add("Email/query", new JsonObject { ["accountId"] = account, ["sort"] = MessageListOrder.ComparatorsJson([new("hasKeyword", true, "$seen"), new("receivedAt", false)]), ["limit"] = 1 });
        var descending = builder.Add("Email/query", new JsonObject { ["accountId"] = account, ["sort"] = MessageListOrder.ComparatorsJson([new("hasKeyword", false, "$seen"), new("receivedAt", false)]), ["limit"] = 1 });
        var get = builder.Add("Email/get", new JsonObject { ["accountId"] = account, ["#ids"] = ascending.Ref("/ids"), ["properties"] = new JsonArray("id", "keywords") });
        var response = await client.SendAsync(builder.Build(), ct).ConfigureAwait(false);
        if (!response.IsOk) return;
        var a = response.Value!.Args(ascending.CallId, ascending.Method);
        var d = response.Value.Args(descending.CallId, descending.Method);
        if (a.Error is Barbican.Core.Jmap.Errors.MethodError { Type: "unsupportedSort" } || d.Error is Barbican.Core.Jmap.Errors.MethodError { Type: "unsupportedSort" }) state.Unsupported = true;
        if (!a.IsOk || !d.IsOk) return;
        var ascendingIds = a.Value!.Strings("ids");
        var first = ascendingIds.Count > 0 ? ascendingIds[0] : null;
        var descendingIds = d.Value!.Strings("ids");
        var last = descendingIds.Count > 0 ? descendingIds[0] : null;
        if (first is null || last is null || string.Equals(first, last, StringComparison.Ordinal)) return;
        var mail = response.Value.Args(get.CallId, get.Method);
        if (mail.IsOk && mail.Value!.Arr("list")?.FirstOrDefault() is JsonObject item && string.Equals(item.Str("id"), first, StringComparison.Ordinal))
        {
            state.Inverted = (item["keywords"] as JsonObject)?.True("$seen") == true;
        }
    }

    private static State StateOf(IJmapClient client, string? accountId) =>
        States.GetValue(client, static _ => new ConcurrentDictionary<string, State>(StringComparer.Ordinal))
            .GetOrAdd(accountId ?? client.Session.MailAccountId, static _ => new State());

    private sealed class State : IDisposable
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public JmapSession? Session { get; set; }
        public volatile bool Unsupported;
        public bool? Inverted { get; set; }
        public DateTimeOffset RetryAfter { get; set; }
        public void Dispose() => Gate.Dispose();
    }
}
