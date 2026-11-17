// Mirrors: Rust templates.rs (Bulwark EmailTemplate shape); Bulwark lib/template-utils.ts, stores/template-store.ts; RFC 9670 (JMAP Metadata is not RFC-numbered here: Stalwart urn:ietf:params:jmap:metadata). Contract from the M3 plan section 4.3; owner send-pipeline.

namespace Barbican.Core.Compose;

/// <summary>A template shared with Bulwark (Inbox metadata <c>org.bulwarkmail.template.&lt;id&gt;</c>).</summary>
public sealed record MailTemplate(string Id, string Name, string Subject, string Body, bool IsHtml)
{
    public string Category { get; init; } = "";

    public bool IsFavorite { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
