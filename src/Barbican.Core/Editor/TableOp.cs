// Mirrors: Rust src/rte/editor.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap commands and input rules). No RFC or Stalwart counterpart. Contract from the M3 plan (stream editor-core).

namespace Barbican.Core.Editor;

/// <summary>Table edits at the caret's cell (Rust <c>table_op</c> strings: row-above, row-below, col-left, col-right, del-row, del-col, del-table, header).</summary>
public enum TableOp
{
    RowAbove,
    RowBelow,
    ColLeft,
    ColRight,
    DelRow,
    DelCol,
    DelTable,
    ToggleHeader,
}
