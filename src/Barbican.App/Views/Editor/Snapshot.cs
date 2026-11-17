// Mirrors: see IEditorClipboardBackend.

using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Editor;

/// <summary>A read-only copy of the clipboard taken before a paste.</summary>
public sealed class Snapshot : IEditorClipboard
{
    public string? Html { get; set; }

    public string? Text { get; set; }

    public IReadOnlyList<ClipboardFile> Files { get; set; } = [];

    public ClipboardFile? Image { get; set; }

    public void SetContent(string html, string text)
    {
        Html = html;
        Text = text;
    }
}
