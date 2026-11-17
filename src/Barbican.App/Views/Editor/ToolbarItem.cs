// Mirrors: Bulwark components/email/rich-text-editor.tsx (toolbar buttons and their active state).

using Barbican.Presentation.Editor;

namespace Barbican.App.Views.Editor;

/// <summary>One toolbar button: how to build it, how its look follows the <see cref="ToolbarState"/>, and which picker it opens (dropdowns).</summary>
public sealed class ToolbarItem(string id, Func<Button> create, Action<Button, ToolbarState> refresh)
{
    private Button? _live;

    public string Id { get; } = id;

    /// <summary>The picker kind a dropdown button opens, or null for a command button.</summary>
    public string? Picker { get; init; }

    /// <summary>The button on the toolbar itself.</summary>
    public Button Live => _live ?? throw new InvalidOperationException("The item is not bound to a toolbar yet.");

    public Func<Button> Create { get; } = create;

    public Action<Button, ToolbarState> Refresh { get; } = refresh;

    internal void Bind(EditorToolbarView host)
    {
        _live = Create();
        if (Picker is not null)
        {
            _live.Click += (_, _) => host.InvokeItem(this, _live);
        }
    }
}
