// Mirrors: Rust compose.rs:134 open (fields, quote, signature, identity), :1155 area; Bulwark components/email/email-composer.tsx; Bulwark lib/reply-identity.ts, lib/email-composer-utils.ts. Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Editor;
using Barbican.Core.Jmap.Submission;

namespace Barbican.Core.Compose;

/// <summary>What a composer starts with: the result of <see cref="IComposeSeeder.SeedAsync"/> (fields, quoted body with signature, identity, attachments).</summary>
public sealed record ComposeSeed
{
    public required string AccountId { get; init; }

    public ComposeKind Kind { get; init; }

    public string To { get; init; } = "";

    public string Cc { get; init; } = "";

    public string Bcc { get; init; } = "";

    public string Subject { get; init; } = "";

    public required Doc Body { get; init; }

    /// <summary>The identity the reply or draft should come from; null means the default identity.</summary>
    public string? IdentityId { get; init; }

    public FromOverride? FromOverride { get; init; }

    public IReadOnlyList<DraftAttachment> Attachments { get; init; } = [];

    /// <summary>Set when the seed reopens a draft.</summary>
    public string? DraftId { get; init; }

    public string? SourceEmailId { get; init; }

    public string? InReplyTo { get; init; }

    public IReadOnlyList<string> References { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    /// <summary>The signature texts the pipeline put in the body, so "blank draft" and the autosave baseline can ignore them (<see cref="ComposeRules.IsBlank"/>).</summary>
    public IReadOnlyList<string> SignatureTexts { get; init; } = [];

    /// <summary>The seed was a saved draft: what it holds counts as already on the server (<see cref="AutosaveState"/>).</summary>
    public bool IsSaved { get; init; }
}
