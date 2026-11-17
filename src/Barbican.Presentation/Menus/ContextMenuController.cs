// Mirrors: Rust ctxmenu.rs request :150 / reopen :113 / hover :134 / confirm :697 / pick :830; spec/menus.md conventions (a flyout opens beside its row and hovering
// another row closes it; a danger row with a confirmation label swaps the menu for "Are you sure?" + the danger row + Cancel); Bulwark components/email/email-context-menu.tsx
// (keyboard: arrows, Home, End, Enter, Escape, Left and Right for flyouts, type-ahead). The in-window host (App) draws it; nothing here touches a window. Owner menus.

using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Menus;

/// <summary>The one open context menu: its rows, flyout, highlighted row and confirmation step, and what happens when a row is picked.</summary>
public sealed class ContextMenuController : ObservableObject, IContextMenuService, IModalSurface
{
    private readonly MenuModelBuilder _builder;
    private readonly ICommandRegistry _registry;
    private readonly CommandRunner _runner;
    private MenuContext? _context;
    private MenuLevel? _root;
    private MenuLevel? _sub;
    private MenuItemSpec? _pending;
    private MenuAnchor _anchor;

    public ContextMenuController(MenuModelBuilder builder, ICommandRegistry registry, IToastService? toasts = null, CommandRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        _builder = builder;
        _registry = registry;
        _runner = runner ?? new CommandRunner(toasts);
    }

    /// <summary>The menu opened or closed, or its rows or highlight changed (the host redraws).</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsOpen => _root is not null;

    public MenuAnchor Anchor => _anchor;

    public MenuContext? Context => _context;

    /// <summary>The menu itself (or the confirmation in its place).</summary>
    public MenuLevel? Root => _root;

    /// <summary>The open flyout, or null.</summary>
    public MenuLevel? Flyout => _sub;

    /// <summary>The confirmation step is showing in place of the menu.</summary>
    public bool Confirming => _pending is not null;

    /// <inheritdoc />
    public void Show(MenuContext context, MenuAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(context);
        var rows = _builder.Build(context);
        if (rows.Count == 0)
        {
            Close();
            return;
        }

        _context = context;
        _anchor = anchor;
        _pending = null;
        _sub = null;
        _root = new MenuLevel(rows, -1);
        Raise();
    }

    /// <inheritdoc />
    public void Close()
    {
        if (_root is null)
        {
            return;
        }

        _root = null;
        _sub = null;
        _pending = null;
        _context = null;
        Raise();
    }

    /// <summary>The pointer is over a row: it is highlighted; a flyout row opens its flyout, any other row closes it (ctxmenu.rs:134).</summary>
    public void Hover(int index, bool inFlyout)
    {
        var level = inFlyout ? _sub : _root;
        if (level is null || index < 0 || index >= level.Items.Count)
        {
            return;
        }

        level.Highlight = MenuLevel.Selectable(level.Items[index]) ? index : -1;
        if (!inFlyout)
        {
            var item = level.Items[index];
            if (item.Kind == MenuItemKind.Submenu && item.Enabled)
            {
                OpenFlyout(index);
            }
            else
            {
                _sub = null;
            }
        }

        Raise();
    }

    /// <summary>Picks a row: a flyout row opens it, a danger row with a confirmation label swaps in the confirmation, any other row runs its command and closes the menu.</summary>
    public async Task<bool> ActivateAsync(int index, bool inFlyout)
    {
        var level = inFlyout ? _sub : _root;
        if (level is null || _context is null || index < 0 || index >= level.Items.Count)
        {
            return false;
        }

        var item = level.Items[index];
        if (!MenuLevel.Selectable(item))
        {
            return false;
        }

        if (item.Kind == MenuItemKind.Submenu)
        {
            OpenFlyout(index);
            Raise();
            return true;
        }

        if (item.ConfirmLabel is not null && _pending is null)
        {
            BeginConfirm(item);
            return true;
        }

        if (item.CommandId is null)
        {
            // the Cancel row of a confirmation
            Close();
            return true;
        }

        var context = _context;
        Close();
        await RunAsync(item, context).ConfigureAwait(true);
        return true;
    }

    /// <summary>The label the user would pick by name (used by automation and tests): searches the menu, then an open or openable flyout.</summary>
    public async Task<bool> PickAsync(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (_root is null)
        {
            return false;
        }

        foreach (var (level, inFlyout) in new[] { (_sub, true), (_root, false) })
        {
            var at = IndexOf(level, label);
            if (at >= 0)
            {
                return await ActivateAsync(at, inFlyout).ConfigureAwait(true);
            }
        }

        // a row of a closed flyout: open the flyout that holds it, then pick
        for (var i = 0; i < _root.Items.Count; i++)
        {
            if (_root.Items[i].Kind != MenuItemKind.Submenu || !_root.Items[i].Enabled)
            {
                continue;
            }

            var rows = _builder.Build(_context! with { MenuId = _root.Items[i].SubmenuId! });
            if (rows.Any(r => string.Equals(r.Label.Trim(), label, StringComparison.Ordinal)))
            {
                OpenFlyout(i);
                return await ActivateAsync(IndexOf(_sub, label), inFlyout: true).ConfigureAwait(true);
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool HandleKey(KeyChord chord)
    {
        if (_root is null)
        {
            return false;
        }

        var level = _sub ?? _root;
        switch (Name(chord.Key))
        {
            case "escape":
                Close();
                return true;
            case "down":
                level.Move(1);
                break;
            case "up":
                level.Move(-1);
                break;
            case "home":
                level.Edge(last: false);
                break;
            case "end":
                level.Edge(last: true);
                break;
            case "right":
                if (_sub is null && _root.Highlight >= 0 && _root.Items[_root.Highlight].Kind == MenuItemKind.Submenu)
                {
                    OpenFlyout(_root.Highlight);
                }

                break;
            case "left":
                if (_sub is not null)
                {
                    _sub = null;
                }

                break;
            case "enter" or "space":
                if (level.Highlight >= 0)
                {
                    _ = ActivateAsync(level.Highlight, ReferenceEquals(level, _sub));
                }

                return true;
            case var other when other.Length == 1 && !chord.Control && !chord.Alt:
                level.TypeAhead(other[0]);
                break;
            default:
                return true;
        }

        Raise();
        return true;
    }

    private void OpenFlyout(int index)
    {
        if (_root is null || _context is null)
        {
            return;
        }

        var spec = _root.Items[index];
        // built when opened: its content and enable state are read now
        var rows = _builder.Build(_context with { MenuId = spec.SubmenuId ?? string.Empty });
        _root.Highlight = index;
        _sub = rows.Count == 0 ? null : new MenuLevel(rows, index);
    }

    private void BeginConfirm(MenuItemSpec item)
    {
        _pending = item;
        _sub = null;
        var run = item with { Label = item.ConfirmLabel!, ConfirmLabel = null, Hint = null };
        _root = new MenuLevel(
            [
                MenuItems.Header("Are you sure?"),
                run,
                MenuItems.Item(MenuGlyphs.Cancel, "Cancel", string.Empty, "Esc") with { CommandId = null },
            ],
            -1)
        { Highlight = 1 };
        Raise();
    }

    private async Task RunAsync(MenuItemSpec item, MenuContext context)
    {
        if (!_registry.TryGet(item.CommandId!, out var command))
        {
            return;
        }

        // a failed result is toasted, a thrown exception too and it goes to the crash log (Fable review 3 R3-05); a cancelled run says nothing
        _ = await _runner.RunAsync(command, MenuModelBuilder.CommandContextOf(context, item), toastFailure: true).ConfigureAwait(true);
    }

    private static int IndexOf(MenuLevel? level, string label)
    {
        if (level is null)
        {
            return -1;
        }

        for (var i = 0; i < level.Items.Count; i++)
        {
            if (level.Items[i].Kind is MenuItemKind.Item or MenuItemKind.Submenu && string.Equals(level.Items[i].Label.Trim(), label, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Name(string key) => key.ToLowerInvariant() switch
    {
        "arrowdown" or "down" => "down",
        "arrowup" or "up" => "up",
        "arrowleft" or "left" => "left",
        "arrowright" or "right" => "right",
        "esc" or "escape" => "escape",
        "return" or "enter" => "enter",
        " " or "space" => "space",
        var k => k,
    };

    private void Raise()
    {
        OnPropertyChanged(nameof(IsOpen));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
