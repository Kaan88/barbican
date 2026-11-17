// Mirrors: Rust unified.rs:242 users (the active login only, or every login with unifiedCrossAccount, the active first), :251 sources (a
// login's own account, plus its shared accounts with includeGroupInUnified), :270 dedupe (Bulwark dedupeUnifiedAccounts: an account
// reachable through several logins is read once, by server and JMAP id; a login's own account wins, then the first login). Owner unified.

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap;

namespace Barbican.Core.Mail.Unified;

/// <summary>The accounts the unified inbox reads.</summary>
public static class UnifiedSources
{
    /// <summary>
    /// The sources of the connected logins (<paramref name="logins"/>: account key and client, the active login first), read as the settings
    /// say; logins without a client are skipped.
    /// </summary>
    public static IReadOnlyList<UnifiedSource> Build(IReadOnlyList<(string Login, IJmapClient? Client)> logins, string? active, UnifiedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(logins);
        ArgumentNullException.ThrowIfNull(settings);
        var users = settings.CrossAccount
            ? logins.OrderBy(l => string.Equals(l.Login, active, StringComparison.Ordinal) ? 0 : 1).ToList()
            : logins.Where(l => string.Equals(l.Login, active, StringComparison.Ordinal)).ToList();
        var all = new List<UnifiedSource>();
        foreach (var (login, client) in users)
        {
            if (client is null || client.Session.MailAccountId.Length == 0)
            {
                continue;
            }

            var own = client.Session.MailAccountId;
            all.Add(new UnifiedSource(login, client, own, Shared: false));
            if (settings.IncludeGroup && SessionJson(client.Session) is { } session)
            {
                all.AddRange(UnifiedRows.SharedAccounts(session, own).Select(a => new UnifiedSource(login, client, a, Shared: true)));
            }
        }

        return UnifiedRows.DedupeBy(all, s => s.Shared, s => (Server(s.Client), s.AccountId));
    }

    private static string Server(IJmapClient c) =>
        (c.Session.ApiUrl.Length > 0 && Uri.TryCreate(c.Session.ApiUrl, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) : c.Session.ServerUrl)
            .TrimEnd('/').ToLowerInvariant();

    private static JsonNode? SessionJson(JmapSession session)
    {
        if (session.Raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(session.Raw.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
