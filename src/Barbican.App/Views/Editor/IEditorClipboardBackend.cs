// Mirrors: Rust src/rte/mod.rs copy/paste (arboard). The WinRT clipboard is asynchronous, so a paste first takes a snapshot and then runs the synchronous EditorClipboardActions on it.

using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Editor;

/// <summary>Where the editor's clipboard actions read and write: the OS clipboard, or (hidden automation runs) an in-process one that never touches the desktop's.</summary>
public interface IEditorClipboardBackend
{
    /// <summary>What is on the clipboard now.</summary>
    Task<IEditorClipboard> SnapshotAsync();

    void Write(string html, string text);
}
