// Mirrors: Rust src/rte/mod.rs copy/paste (arboard: set_html, get().html(), get().file_list(), get_image(), get_text()). The seam that keeps tests off the desktop's real clipboard.

namespace Barbican.Presentation.Editor;

/// <summary>What the editor reads from and writes to the clipboard.</summary>
public interface IEditorClipboard
{
    string? Html { get; }

    string? Text { get; }

    /// <summary>Files copied in a file manager.</summary>
    IReadOnlyList<ClipboardFile> Files { get; }

    /// <summary>A bitmap on the clipboard (a screenshot), as PNG.</summary>
    ClipboardFile? Image { get; }

    void SetContent(string html, string text);
}
