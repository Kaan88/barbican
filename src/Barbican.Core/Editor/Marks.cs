// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

namespace Barbican.Core.Editor;

/// <summary>Inline formatting of a run of text. Immutable; change it with <c>with</c> expressions.</summary>
public sealed record Marks
{
    /// <summary>No formatting.</summary>
    public static Marks Plain { get; } = new();

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Strike { get; init; }

    public bool Code { get; init; }

    public string? Link { get; init; }

    public string? Color { get; init; }

    public string? Highlight { get; init; }

    /// <summary>Font size in px; null means the body size.</summary>
    public int? Size { get; init; }

    public bool IsPlain => Equals(Plain);
}
