// Bulwark lib/admin/policy-subject.ts and lib/jmap/client.ts getPrincipalTypes; RFC 9670 Principal/get.
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Builds a complete policy subject from an exact, pinned JMAP session.</summary>
internal static class OrganizationPolicySubjectSource
{
    private const string Principals = "urn:ietf:params:jmap:principals";
    private const int MaxCandidates = 128;

    internal static async Task<OrganizationPolicySubject?> ReadAsync(JmapClient client, JmapSession pinned, Func<Func<bool>, bool> beforeSend, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(pinned);
        ArgumentNullException.ThrowIfNull(beforeSend);
        if (!ReferenceEquals(client.Session, pinned) || !TryReadAccounts(pinned, out var email, out var candidates)) return null;
        if (candidates.Count == 0) return new(email, []);
        if (!pinned.Has(Principals) || pinned.Limits.MaxObjectsInGet <= 0) return null;
        var groups = new List<string>();
        foreach (var batch in candidates.Chunk(Math.Min(MaxCandidates, pinned.Limits.MaxObjectsInGet)))
        {
            var args = new JsonObject { ["accountId"] = pinned.MailAccountId,
                ["ids"] = new JsonArray([.. batch.Select(item => (JsonNode?)JsonValue.Create(item.Id))]),
                ["properties"] = new JsonArray("id", "type") };
            var request = JmapRequest.Of(JmapRequest.Call("Principal/get", args, "p")) with { Using = [JmapCapabilities.Core, Principals], BeforeSend = (issue, token) => Task.FromResult(!token.IsCancellationRequested && beforeSend(() => ReferenceEquals(client.Session, pinned) && issue())) };
            var result = await client.SendAsync(request, ct).ConfigureAwait(false);
            if (!ReferenceEquals(client.Session, pinned) || !result.IsOk) return null;
            var responses = result.Value!.MethodResponses.Where(row => row.Count == 3 && string.Equals(row[2].AsString(), "p", StringComparison.Ordinal)).ToArray();
            if (responses.Length != 1 || !string.Equals(responses[0][0].AsString(), "Principal/get", StringComparison.Ordinal) || responses[0][1] is not JsonObject body ||
                !string.Equals(body.Str("accountId"), pinned.MailAccountId, StringComparison.Ordinal) || body["list"] is not JsonArray list ||
                body["notFound"] is not JsonArray { Count: 0 } || list.Count != batch.Length) return null;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                if (item is not JsonObject principal || principal.Str("id") is not { Length: > 0 } id ||
                    principal.Str("type") is not { Length: > 0 } type || type is not ("individual" or "group" or "resource" or "location" or "other") ||
                    !found.Add(id) || !batch.Any(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))) return null;
                if (string.Equals(type, "group", StringComparison.Ordinal)) groups.Add(batch.Single(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal)).Address);
            }
        }
        return ReferenceEquals(client.Session, pinned) ? new OrganizationPolicySubject(email, [.. groups.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]) : null;
    }

    private static bool TryReadAccounts(JmapSession session, out string email, out List<(string Id, string Address)> candidates)
    {
        email = string.Empty;
        candidates = [];
        if (!UniqueObject(session.Raw) || !session.Raw.TryGetProperty("accounts", out var accounts) || !UniqueObject(accounts) ||
            string.IsNullOrWhiteSpace(session.MailAccountId) || !accounts.TryGetProperty(session.MailAccountId, out var primary) || !UniqueObject(primary)) return false;
        var username = session.Username.Trim();
        if (!username.Contains('@', StringComparison.Ordinal))
        {
            if (!primary.TryGetProperty("name", out var primaryName) || primaryName.ValueKind != JsonValueKind.String) return false;
            username = primaryName.GetString()?.Trim() ?? string.Empty;
        }
        if (!RecipientText.IsValidEmail(username)) return false;
        email = RecipientText.ToAsciiEmail(username).ToLowerInvariant();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in accounts.EnumerateObject())
        {
            if (!ids.Add(account.Name) || account.Name.Length is 0 or > 1024 || !UniqueObject(account.Value)) return false;
            if (string.Equals(account.Name, session.MailAccountId, StringComparison.Ordinal)) continue;
            if (!account.Value.TryGetProperty("isPersonal", out var personal) || personal.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            if (personal.GetBoolean()) continue;
            if (!account.Value.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) return false;
            var address = name.GetString()?.Trim() ?? string.Empty;
            if (!address.Contains('@', StringComparison.Ordinal)) continue;
            if (!RecipientText.IsValidEmail(address)) return false;
            candidates.Add((account.Name, RecipientText.ToAsciiEmail(address).ToLowerInvariant()));
            if (candidates.Count > MaxCandidates) return false;
        }
        return true;
    }
    private static bool UniqueObject(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) if (!names.Add(property.Name)) return false;
        return true;
    }

}
