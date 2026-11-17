// Mirrors: CLAUDE.md (no test touches the desktop clipboard); the backend is the OS one, replaced by an in-process one in the automation build.

namespace Barbican.App.Views.Editor;

/// <summary>The clipboard backend every editor uses.</summary>
public static class EditorClipboards
{
    public static IEditorClipboardBackend Backend { get; set; } = new OsClipboardBackend();
}
