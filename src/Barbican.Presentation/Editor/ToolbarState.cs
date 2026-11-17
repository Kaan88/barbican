// Mirrors: M3 plan section 4.6; Rust rte/mod.rs refresh :244, ctx_info :332 (CtxInfo); Bulwark components/email/email-composer.tsx (toolbar state). Contract from the M3 plan; owner editor-view.

using Barbican.Core.Editor;

namespace Barbican.Presentation.Editor;

/// <summary>What the toolbar, the bubble and the context menu show for the current selection (computed by the view model after every change).</summary>
public sealed record ToolbarState
{
    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Underline { get; init; }

    public bool Strike { get; init; }

    public bool Code { get; init; }

    public bool Highlight { get; init; }

    public string? Link { get; init; }

    public string? Color { get; init; }

    public int? Size { get; init; }

    public BlockKind Block { get; init; } = new BlockKind.Para();

    public Align Align { get; init; }

    public bool CanUndo { get; init; }

    public bool CanRedo { get; init; }

    public bool InTable { get; init; }

    public bool OnImage { get; init; }

    public bool HasSelection { get; init; }

    public bool OnMisspelledWord { get; init; }
}
