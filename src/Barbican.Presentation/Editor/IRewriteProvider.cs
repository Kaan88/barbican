// Mirrors: Rust rte/mod.rs ai_source :93, ai_replace :103; spec/menus.md MENU-RTE (Rewrite with AI); Bulwark components/email/email-composer.tsx AI rewrite. Contract from the M3 plan; owner editor-view (the assistant, M6, implements it).

namespace Barbican.Presentation.Editor;

/// <summary>The "Rewrite with AI" seam of the editor's context menu: Improve, Shorten, Formal, Casual, Fix spelling, Translate. M3 ships the menu rows disabled (<see cref="IsAvailable"/> false).</summary>
public interface IRewriteProvider
{
    bool IsAvailable { get; }

    /// <summary>The rewritten text for <paramref name="action"/>, or null when the assistant declined.</summary>
    Task<string?> RewriteAsync(string action, string text, CancellationToken ct);
}
