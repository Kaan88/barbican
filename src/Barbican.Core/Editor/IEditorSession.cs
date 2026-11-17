// Mirrors: Rust src/rte/editor.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap commands and input rules). No RFC or Stalwart counterpart. Contract from the M3 plan (stream editor-core).

namespace Barbican.Core.Editor;

/// <summary>
/// The editing session the editor view (stream editor-view) and the composer (stream composer-ui) drive: the document, the selection, undo and every command
/// of Rust's <c>Editor</c>. Layout-independent: cursor movement that needs line geometry (up/down, home/end of a visual line) stays in the view, which
/// calls <see cref="SetCaret"/>/<see cref="SelectRange"/> and keeps <see cref="GoalX"/>. Implemented by <see cref="EditorSession"/> (stream editor-core); the view and the
/// composer code against this interface so they can be tested with a fake. Not thread-safe: UI thread only.
/// </summary>
public interface IEditorSession
{
    /// <summary>Raised after every change of the document, the selection or the history (the view relayouts and repaints).</summary>
    event EventHandler? Changed;

    Doc Doc { get; }

    Sel Sel { get; }

    /// <summary>Marks toggled at a collapsed caret, applied to the next typed text.</summary>
    Marks? Stored { get; }

    /// <summary>Preferred x for vertical caret movement (the view's).</summary>
    float? GoalX { get; set; }

    /// <summary>Bumped on every change (drives relayout and the composer's "dirty" checks).</summary>
    long Version { get; }

    /// <summary>Plain text only: no markdown-style input rules, no marks (Bulwark's plainTextMode).</summary>
    bool Plain { get; set; }

    bool CanUndo { get; }

    bool CanRedo { get; }

    /// <summary>Images in the document with their upload state (those without a <see cref="ImageData.BlobId"/> still need an upload).</summary>
    IReadOnlyList<ImageData> InlineImages { get; }

    /// <summary>Replaces the whole document (opening the composer); clears history.</summary>
    void Load(Doc doc);

    void Checkpoint(EditKind kind);

    void BreakGroup();

    bool Undo();

    bool Redo();

    void SetCaret(Pos p);

    void SelectRange(Pos anchor, Pos focus);

    void SelectAll();

    string SelectedText();

    IReadOnlyList<Block> SelectedBlocks();

    void SelectWord(Pos p);

    void SelectBlock(int block);

    /// <summary>The marks a typed character would get: the stored ones at a caret, else those of the selection start.</summary>
    Marks CurrentMarks();

    void InsertText(string text);

    void Enter(bool shift);

    void Backspace(bool word);

    void DeleteForward(bool word);

    void ToggleMark(Mark mark);

    void SetColor(string? color);

    void SetHighlight(string? color);

    void SetSize(int? size);

    void SetBlock(BlockKind kind);

    void SetAlign(Align align);

    /// <summary>Indents or outdents list items; false when nothing changed.</summary>
    bool Indent(int delta);

    void ClearFormat();

    /// <summary>The link under the caret or selection: block, start, end, url.</summary>
    (int Block, int Start, int End, string Url)? LinkRange();

    string? CurrentLink();

    /// <summary>Sets (or, with an empty url, removes) the link; a bare host gets https://. <paramref name="text"/> is the link text when nothing is selected.</summary>
    void SetLink(string url, string text);

    void InsertBlocks(IReadOnlyList<Block> blocks);

    void InsertImage(ImageData image);

    void InsertRule();

    void InsertTable(int rows, int cols);

    /// <summary>A table edit at the caret's cell; false when the caret is not in a table.</summary>
    bool ApplyTable(TableOp op);

    /// <summary>Tab / Shift+Tab inside a table: move between cells (adding a row at the end); false outside a table.</summary>
    bool CellTab(bool back);

    /// <summary>Replaces the selection (or, with <paramref name="whole"/>, the editable body) with plain text; the quoted original and what follows it stay.</summary>
    void ReplaceText(string text, bool whole);

    /// <summary>Resizes the image block; <paramref name="undo"/> false merges into the previous step (one undo step per drag). False when the block is no image.</summary>
    bool ResizeImage(int block, int width, int height, bool undo);

    void ReplaceDoc(Doc doc, bool undo);

    /// <summary>Replaces the text between two positions of one block (spelling suggestions).</summary>
    void ReplaceRange(Pos a, Pos b, string text);

    void ToggleQuoted(int block);

    /// <summary>Records the upload of an inline image (matched by cid): blob id, type and name (Rust <c>set_image_info</c>).</summary>
    bool SetImageUploaded(string cid, string blobId, string mime, string name);
}
