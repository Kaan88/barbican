// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:40-66 sourceAccount (an explicit account must be the signed-in login's own or one
// the login shows; otherwise the message is not this login's to read) and lib/plugin-sandbox/account-kind.ts (only non-personal session
// accounts are shared/group). The captured session's JMAP account list decides; the plugin's claim never does.
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Unified;

namespace Barbican.Core.Plugins.Api.Mail;

internal static class PluginMailRouting
{
    /// <summary>Where a read goes: <see cref="SharedAccountId"/> null is the login's own account.</summary>
    internal readonly record struct Route(bool Allowed, string? SharedAccountId, string AccountId);

    internal static Route Resolve(IAccountSession session, string? requested)
    {
        var own = session.Client.Session.MailAccountId;
        if (requested is null || string.Equals(requested, own, StringComparison.Ordinal)) return new(true, null, own);
        return SharedAccounts(session, own).Contains(requested, StringComparer.Ordinal) ? new(true, requested, requested) : new(false, null, own);
    }

    private static IReadOnlyList<string> SharedAccounts(IAccountSession session, string own)
    {
        var raw = session.Client.Session.Raw;
        if (raw.ValueKind != JsonValueKind.Object) return [];
        try
        {
            return JsonNode.Parse(raw.GetRawText()) is { } node ? UnifiedRows.SharedAccounts(node, own) : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
