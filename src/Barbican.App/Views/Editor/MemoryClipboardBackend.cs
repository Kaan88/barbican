// Mirrors: CLAUDE.md "never touch the real mouse, keyboard or screen" and M3 plan 6.3 ("no test touches the OS clipboard of the desktop"): the clipboard of hidden automation runs.

using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Editor;

/// <summary>An in-process clipboard; the automation commands can fill it with files and bitmaps.</summary>
public sealed class MemoryClipboardBackend : IEditorClipboardBackend
{
    private readonly Snapshot _state = new();

    public Task<IEditorClipboard> SnapshotAsync() => Task.FromResult<IEditorClipboard>(new Snapshot { Html = _state.Html, Text = _state.Text, Files = _state.Files, Image = _state.Image });

    public void Write(string html, string text) => _state.SetContent(html, text);

    /// <summary>Puts files (or a picture) on the clipboard as a file manager or a screenshot tool would.</summary>
    public void Put(string? html, string? text, IReadOnlyList<ClipboardFile> files, ClipboardFile? image)
    {
        _state.Html = html;
        _state.Text = text;
        _state.Files = files;
        _state.Image = image;
    }

    /// <summary>What a paste would read, for the dump.</summary>
    public (string? Html, string? Text) Content => (_state.Html, _state.Text);
}
