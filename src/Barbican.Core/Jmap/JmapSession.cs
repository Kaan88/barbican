// Mirrors: RFC 8620 §2 (the Session resource: capabilities, accounts, primaryAccounts, username, apiUrl, downloadUrl, uploadUrl,
// eventSourceUrl, state); Rust jmap.rs:139 open (primary mail account required), :299 account_for, :306 account_cap, :313 limit;
// Bulwark lib/jmap/client.ts:2150 connect (a session without accounts, apiUrl and downloadUrl is no sign-in), :7391
// getEventSourceUrl (falls back to the core capability's eventSourceUrl); Stalwart crates/jmap/src/api/session.rs. Owner jmap.

using System.Text.Json;

namespace Barbican.Core.Jmap;

/// <summary>
/// The JMAP session object. Immutable: the client swaps in a new one when <c>sessionState</c> changes. The raw document is kept as a
/// <see cref="JsonElement"/> (safe to read from several threads), so capabilities a later milestone needs are still there.
/// </summary>
public sealed record JmapSession
{
    /// <summary>The API URL, made absolute (RFC 8620 §2).</summary>
    public string ApiUrl { get; init; } = "";

    /// <summary>The session state (changes when the session resource changes).</summary>
    public string State { get; init; } = "";

    /// <summary>The download URL template, made absolute (RFC 8620 §6.2).</summary>
    public string DownloadUrl { get; init; } = "";

    /// <summary>The upload URL template, made absolute (RFC 8620 §6.1).</summary>
    public string UploadUrl { get; init; } = "";

    /// <summary>The event source URL template, made absolute, or empty when the server has no push (RFC 8620 §7.3).</summary>
    public string EventSourceUrl { get; init; } = "";

    /// <summary>The login the session belongs to.</summary>
    public string Username { get; init; } = "";

    /// <summary>The URL the session was read from (after redirects), for the re-fetch on a <c>sessionState</c> change.</summary>
    public string SessionUrl { get; init; } = "";

    /// <summary>The server address the user typed (its origin is where every session URL was moved; Fable review 1 F-02).</summary>
    public string ServerUrl { get; init; } = "";

    /// <summary>
    /// The origins (scheme://host[:port]) the Authorization header may be sent to: the typed server's, plus any https host the server named
    /// for downloads, uploads or push (Bulwark lib/jmap/client.ts:2347). Empty: no request carries credentials.
    /// </summary>
    public IReadOnlyList<string> CredentialOrigins { get; init; } = [];

    /// <summary>The account id per capability URN (RFC 8620 §2 primaryAccounts).</summary>
    public IReadOnlyDictionary<string, string> PrimaryAccounts { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The capability URNs the server advertises.</summary>
    public IReadOnlyList<string> CapabilityNames { get; init; } = [];

    /// <summary>The core limits (RFC 8620 §2 urn:ietf:params:jmap:core).</summary>
    public CoreLimits Limits { get; init; } = new();

    /// <summary>The whole session document.</summary>
    public JsonElement Raw { get; init; }

    /// <summary>The primary mail account (required: a login without one is refused at connect).</summary>
    public string MailAccountId => PrimaryAccounts.TryGetValue(JmapCapabilities.Mail, out var id) ? id : "";

    /// <summary>True for Stalwart (its <c>urn:stalwart:jmap</c> capability), whose state strings can be decoded.</summary>
    public bool IsStalwart => Has(JmapCapabilities.Stalwart);

    /// <summary>True when a request to <paramref name="url"/> may carry this login's credentials.</summary>
    public bool AllowsCredentials(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || !string.IsNullOrEmpty(url.UserInfo))
        {
            return false;
        }

        var origin = url.GetLeftPart(UriPartial.Authority);
        return CredentialOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the server advertises <paramref name="capability"/>.</summary>
    public bool Has(string capability) => CapabilityNames.Contains(capability, StringComparer.Ordinal);

    /// <summary>The primary account for a capability, else the mail account (Rust account_for).</summary>
    public string AccountFor(string capability) =>
        PrimaryAccounts.TryGetValue(capability, out var id) ? id : MailAccountId;

    /// <summary>
    /// A capability's account-level object (RFC 8620 §2 <c>accounts[id].accountCapabilities</c>), falling back to the session-level
    /// one. RFC 8621 §1.3 puts maxDelayedSend and submissionExtensions at the account level; Stalwart publishes an empty session-level
    /// submission object, which is the Rust bug J-004 fixed.
    /// </summary>
    public JsonElement? CapabilityObject(string accountId, string capability)
    {
        if (Raw.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Raw.TryGetProperty("accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Object
            && accounts.TryGetProperty(accountId, out var account) && account.ValueKind == JsonValueKind.Object
            && account.TryGetProperty("accountCapabilities", out var caps) && caps.ValueKind == JsonValueKind.Object
            && caps.TryGetProperty(capability, out var accountLevel) && accountLevel.ValueKind == JsonValueKind.Object)
        {
            return accountLevel;
        }

        return Raw.TryGetProperty("capabilities", out var server) && server.ValueKind == JsonValueKind.Object
            && server.TryGetProperty(capability, out var sessionLevel) && sessionLevel.ValueKind == JsonValueKind.Object
            ? sessionLevel
            : null;
    }
}
