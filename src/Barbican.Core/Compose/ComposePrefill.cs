// Mirrors: Rust mailto.rs:156 fill; Bulwark lib/plugin-hook-catalog.ts onBeforeCompose ComposeOptions; RFC 6068 section 2. Contract from the M3 plan section 4.3; owner send-pipeline (composer-ui fills it from a mailto link or a plugin).

namespace Barbican.Core.Compose;

/// <summary>Fields to put into a new composer: a mailto link or a plugin's <c>onBeforeCompose</c> result. Recipient fields are comma lists in the field-text form.</summary>
public sealed record ComposePrefill
{
    public string To { get; init; } = "";

    public string Cc { get; init; } = "";

    public string Bcc { get; init; } = "";

    public string Subject { get; init; } = "";

    /// <summary>Plain text.</summary>
    public string Body { get; init; } = "";
}
