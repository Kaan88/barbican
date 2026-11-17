using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>An in-memory clipboard: no test touches the desktop's real one.</summary>
internal sealed class FakeEditorClipboard : IEditorClipboard
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
