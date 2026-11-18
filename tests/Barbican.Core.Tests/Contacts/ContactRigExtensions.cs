// Helpers of the contact tests over the fake server.

using System.Text.Json.Nodes;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Contacts;

internal static class ContactRigExtensions
{
    /// <summary>The methods of one name that reached the server, counted over every request.</summary>
    public static int Count(this FakeJmapServer server, string method) => server.ApiCalls.Sum(c => c.Split(',').Count(m => string.Equals(m, method, StringComparison.Ordinal)));

    public static JsonObject Card(this FakeContacts contacts, string id) => contacts.Cards().First(c => string.Equals((string?)c["id"], id, StringComparison.Ordinal));

    /// <summary>Waits until <paramref name="count"/> payloads of an observer hook (fired without waiting for the observer) matching <paramref name="filter"/> were seen; the last one.</summary>
    public static async Task<object?> PayloadAsync(this ContactHooks hooks, string hook, int count = 1, Func<object?, bool>? filter = null)
    {
        List<(string Hook, object? Payload)> match = [];
        await Jmap.Poll.UntilAsync(
            () =>
            {
                match = [.. hooks.Seen.Where(h => string.Equals(h.Hook, hook, StringComparison.Ordinal) && (filter is null || filter(h.Payload)))];
                return match.Count >= count;
            },
            "hook " + hook);
        return match[^1].Payload;
    }
}
