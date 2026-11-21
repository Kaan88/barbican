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
    /// <summary>The arrow-key step of a focused resize handle: Bulwark resize-handle.tsx steps 10 px.</summary>
    public const float KeyStep = 10f;

    private readonly string _path;
    private readonly IHookBus _hooks;
    private float _side;
    private float _list;
    private float _listHeight;

    public PanesViewModel(string panesFile, IHookBus hooks)
    {
        ArgumentException.ThrowIfNullOrEmpty(panesFile);
        ArgumentNullException.ThrowIfNull(hooks);
        _path = panesFile;
        _hooks = hooks;
        (_side, _list) = PanesFile.Load(panesFile);
        _listHeight = PanesFile.LoadHeight(panesFile);
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

    /// <summary>The list pane height of the stacked (horizontal) mail layout in dips; 0 means the default (Bulwark ui-store.ts:18).</summary>
    public float ListHeight
    {
        get => _listHeight;
        private set => SetProperty(ref _listHeight, value);
    }

    /// <summary>The column name of the stacked layout's handle (the list's height).</summary>
    public const string ListHeightColumn = "email-list-height";

    /// <summary>A resize handle moved (the new width of the column under it; the height for <see cref="ListHeightColumn"/>).</summary>
    public void Resize(string column, float width)
    {
        ArgumentNullException.ThrowIfNull(column);
        if (string.Equals(column, ListHeightColumn, StringComparison.Ordinal))
        {
            ListHeight = PaneLimits.ClampHeight(width);
            return;
        }

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
        else if (string.Equals(column, ListHeightColumn, StringComparison.Ordinal))
        {
            ListHeight = PaneLimits.ListHeightDefault;
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
        PanesFile.Save(_path, _side, _list, _listHeight);
        var width = string.Equals(column, "sidebar", StringComparison.Ordinal) ? _side : string.Equals(column, ListHeightColumn, StringComparison.Ordinal) ? _listHeight : _list;
        _hooks.Fire(HookNames.OnColumnResize, () => new ColumnResizePayload(column, width));
    }
}
