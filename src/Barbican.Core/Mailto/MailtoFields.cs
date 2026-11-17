// Mirrors: Rust mailto.rs:15 Mailto (here MailtoFields: MA0049); Rust mailto.rs (tag rust-final); RFC 6068 (mailto URI); Bulwark app/[locale]/page.tsx mailto handling, lib/mailto.ts. Contract from the M3 plan section 4.5; owner composer-ui.

namespace Barbican.Core.Mailto;

/// <summary>A parsed mailto link: recipient lists (each entry in field-text form) and the subject and plain-text body.</summary>
public sealed record MailtoFields
{
    public IReadOnlyList<string> To { get; init; } = [];

    public IReadOnlyList<string> Cc { get; init; } = [];

    public IReadOnlyList<string> Bcc { get; init; } = [];

    public string Subject { get; init; } = "";

    public string Body { get; init; } = "";

    /// <summary>Parameters named <c>x-&lt;plugin id&gt;-&lt;name&gt;</c> (Bulwark 1.21 onComposeFromLink), keys lower-cased; the composer hands them to the plugin they name.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
