// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>A sending identity (RFC 8621 §6). A wildcard identity has an email such as <c>*@example.org</c>.</summary>
public sealed record Identity(string Id, string? Name, string Email)
{
    /// <summary>Reply-To addresses of the identity (RFC 8621 §6.1 <c>replyTo</c>), or null.</summary>
    public IReadOnlyList<EmailAddress>? ReplyTo { get; init; }

    /// <summary>Server properties not modelled here (signatures, mayDelete, ...), kept for a read-modify-write.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public IDictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }
}
