// Mirrors: Rust dnd.rs:241-270 (SIDE_MIN/MAX, LIST_MIN/MAX, clamp_panes, load_panes once at start, save_panes on drag end; 0 = the style's own width); Bulwark
// components/layout/resize-handle.tsx (arrow keys resize, double-click resets), stores/ui-store.ts; hook onColumnResize; spec I-071, B-DND-009. Owner actions.
//
// The widths are read once at start and applied before the first frame: nothing animates and nothing lays out twice. A drag only changes the width of the column
// under the handle; the file is written when the drag ends.

using Barbican.Core.Layout;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>The widths of the sidebar and the list pane.</summary>
public sealed class PanesViewModel : ObservableObject
{
    /// <summary>The arrow-key step of a focused resize handle.</summary>
    public const float KeyStep = 16f;

    private readonly string _path;
    private readonly IHookBus _hooks;
    private float _side;
    private float _list;

    public PanesViewModel(string panesFile, IHookBus hooks)
    {
        ArgumentException.ThrowIfNullOrEmpty(panesFile);
        ArgumentNullException.ThrowIfNull(hooks);
        _path = panesFile;
        _hooks = hooks;
        (_side, _list) = PanesFile.Load(panesFile);
    }

    /// <summary>The sidebar width in dips; 0 means the style's own width.</summary>
    public float SidebarWidth
    {
        get => _side;
        private set => SetProperty(ref _side, value);
    }

    /// <summary>The list pane width in dips; 0 means the style's own width.</summary>
    public float ListWidth
    {
        get => _list;
        private set => SetProperty(ref _list, value);
    }

    /// <summary>A resize handle moved (the new width of the column under it).</summary>
    public void Resize(string column, float width)
    {
        ArgumentNullException.ThrowIfNull(column);
        var (side, list) = string.Equals(column, "sidebar", StringComparison.Ordinal) ? PaneLimits.Clamp(width, _list) : PaneLimits.Clamp(_side, width);
        SidebarWidth = side;
        ListWidth = list;
    }

    /// <summary>An arrow key on a focused handle: grows or shrinks the column by one step from <paramref name="current"/> (the width on screen).</summary>
    public void Nudge(string column, float current, int direction) => Resize(column, current + (direction * KeyStep));

    /// <summary>Double-click on a handle: back to the style's width.</summary>
    public void Reset(string column)
    {
        if (string.Equals(column, "sidebar", StringComparison.Ordinal))
        {
            SidebarWidth = PaneLimits.SideDefault;
        }
        else
        {
            ListWidth = PaneLimits.ListDefault;
        }

        Commit(column);
    }

    /// <summary>The drag ended: saves panes.json and tells plugins (onColumnResize).</summary>
    public void Commit(string column)
    {
        PanesFile.Save(_path, _side, _list);
        var width = string.Equals(column, "sidebar", StringComparison.Ordinal) ? _side : _list;
        _hooks.Fire(HookNames.OnColumnResize, () => new ColumnResizePayload(column, width));
    }
}
