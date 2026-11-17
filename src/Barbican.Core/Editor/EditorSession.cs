// Mirrors: Rust src/rte/editor.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap commands and input rules). No RFC or Stalwart counterpart. Contract stub from the M3 plan; owner editor-core replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Editor;

/// <summary>Editing commands, input rules and undo on top of <see cref="Doc"/> (port of Rust <c>Editor</c>). Stub: every command throws <see cref="PortPendingException"/> until stream editor-core lands.</summary>
public sealed class EditorSession : IEditorSession
{
    public EditorSession()
        : this(new Doc())
    {
    }

    /// <summary>A session over <paramref name="doc"/>; <paramref name="time"/> times the 1200 ms undo grouping (a fake in tests, the system clock when null).</summary>
    public EditorSession(Doc doc, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        Doc = doc;
        Time = time ?? TimeProvider.System;
        doc.Fix();
    }

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public TimeProvider Time { get; }

    public Doc Doc { get; private set; }

    public Sel Sel { get; private set; }

    public Marks? Stored => null;

    public float? GoalX { get; set; }

    public long Version => 1;

    public bool Plain { get; set; }

    public bool CanUndo => false;

    public bool CanRedo => false;

    public IReadOnlyList<ImageData> InlineImages => [];

    public void Load(Doc doc) => throw Pending("Load");

    public void Checkpoint(EditKind kind) => throw Pending("Checkpoint");

    public void BreakGroup() => throw Pending("BreakGroup");

    public bool Undo() => throw Pending("Undo");

    public bool Redo() => throw Pending("Redo");

    public void SetCaret(Pos p) => throw Pending("SetCaret");

    public void SelectRange(Pos anchor, Pos focus) => throw Pending("Select");

    public void SelectAll() => throw Pending("SelectAll");

    public string SelectedText() => throw Pending("SelectedText");

    public IReadOnlyList<Block> SelectedBlocks() => throw Pending("SelectedBlocks");

    public void SelectWord(Pos p) => throw Pending("SelectWord");

    public void SelectBlock(int block) => throw Pending("SelectBlock");

    public Marks CurrentMarks() => throw Pending("CurrentMarks");

    public void InsertText(string text) => throw Pending("InsertText");

    public void Enter(bool shift) => throw Pending("Enter");

    public void Backspace(bool word) => throw Pending("Backspace");

    public void DeleteForward(bool word) => throw Pending("DeleteForward");

    public void ToggleMark(Mark mark) => throw Pending("ToggleMark");

    public void SetColor(string? color) => throw Pending("SetColor");

    public void SetHighlight(string? color) => throw Pending("SetHighlight");

    public void SetSize(int? size) => throw Pending("SetSize");

    public void SetBlock(BlockKind kind) => throw Pending("SetBlock");

    public void SetAlign(Align align) => throw Pending("SetAlign");

    public bool Indent(int delta) => throw Pending("Indent");

    public void ClearFormat() => throw Pending("ClearFormat");

    public (int Block, int Start, int End, string Url)? LinkRange() => throw Pending("LinkRange");

    public string? CurrentLink() => throw Pending("CurrentLink");

    public void SetLink(string url, string text) => throw Pending("SetLink");

    public void InsertBlocks(IReadOnlyList<Block> blocks) => throw Pending("InsertBlocks");

    public void InsertImage(ImageData image) => throw Pending("InsertImage");

    public void InsertRule() => throw Pending("InsertRule");

    public void InsertTable(int rows, int cols) => throw Pending("InsertTable");

    public bool ApplyTable(TableOp op) => throw Pending("ApplyTable");

    public bool CellTab(bool back) => throw Pending("CellTab");

    public void ReplaceText(string text, bool whole) => throw Pending("ReplaceText");

    public bool ResizeImage(int block, int width, int height, bool undo) => throw Pending("ResizeImage");

    public void ReplaceDoc(Doc doc, bool undo) => throw Pending("ReplaceDoc");

    public void ReplaceRange(Pos a, Pos b, string text) => throw Pending("ReplaceRange");

    public void ToggleQuoted(int block) => throw Pending("ToggleQuoted");

    public bool SetImageUploaded(string cid, string blobId, string mime, string name) => throw Pending("SetImageUploaded");

    private static PortPendingException Pending(string member) => new("EditorSession." + member);
}
