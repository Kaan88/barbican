// Mirrors: spec/menus.md (which menu a right-click opens: a message row MENU-MAIL with the selection as target when the row is in a multi-selection, a folder row MENU-FOLDER, a tag
// MENU-TAG, the list background MENU-LIST); Rust ctxmenu.rs request :150; Bulwark components/email/email-context-menu.tsx. One handler on the window's root (handled events too), so the
// views of other streams need no edit (M2 plan section 3.3). Owner menus.

using Barbican.App.Shell;
using Barbican.App.Views.List;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Lists;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Menus;

/// <summary>Turns a right-click anywhere in the window into the context menu of what is under the pointer.</summary>
public sealed class ContextPointerRouter
{
    private readonly IContextMenuService _menus = ViewHost.Get<IContextMenuService>();
    private readonly MessageListViewModel _list = ViewHost.Get<MessageListViewModel>();
    private readonly SidebarViewModel _sidebar = ViewHost.Get<SidebarViewModel>();
    private readonly IActiveMail _active = ViewHost.Get<IActiveMail>();
    private readonly IMailSelection _selection = ViewHost.Get<IMailSelection>();
    private readonly IMailTargetResolver? _resolver = ViewHost.Find<IMailTargetResolver>();
    private UIElement? _root;
    private DependencyObject? _host;

    /// <summary>Starts listening on the window's content; <paramref name="host"/> is the menu host (its own content is never a target).</summary>
    public void Attach(UIElement root, DependencyObject host)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _host = host;
        root.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler(OnRightTapped), handledEventsToo: true);
    }

    /// <summary>Opens a menu for automation: <paramref name="target"/> is a row index or subject text (mail), a folder id or name, a tag id, an address, or nothing.</summary>
    public string OpenFor(string menuId, string target)
    {
        if (_root is null)
        {
            return "error not attached";
        }

        var anchor = new MenuAnchor(120, 140, null);
        MenuContext? context;
        switch (menuId)
        {
            case MenuIds.Mail or MenuIds.Thread:
                var row = FindRow(target);
                if (row is null)
                {
                    return "error no such row";
                }

                anchor = AnchorOf(row);
                context = MailContext(menuId, row);
                break;
            case MenuIds.Folder:
                var folder = _sidebar.Folders.FirstOrDefault(f => string.Equals(f.Id, target, StringComparison.Ordinal) || string.Equals(f.Name, target, StringComparison.OrdinalIgnoreCase));
                if (folder is null)
                {
                    return "error no such folder";
                }

                anchor = AnchorOf(folder);
                context = FolderContext(folder);
                break;
            case MenuIds.Tag:
                var tag = _sidebar.Tags.FirstOrDefault(t => string.Equals(t.Id, target, StringComparison.Ordinal) || string.Equals(t.Label, target, StringComparison.OrdinalIgnoreCase));
                if (tag is null)
                {
                    return "error no such tag";
                }

                anchor = AnchorOf(tag);
                context = Base(menuId) with { TagId = tag.Id };
                break;
            case MenuIds.Address:
                context = Base(menuId) with { Address = target };
                break;
            case MenuIds.Account:
                context = Base(menuId) with { Address = target.Length > 0 ? target : _active.AccountKey };
                break;
            default:
                context = Base(menuId);
                break;
        }

        _menus.Show(context, anchor);
        return _menus.IsOpen ? "ok" : "error no rows";
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        if (_host is not null && VisualWalk.IsInside(source, _host))
        {
            return;
        }

        var anchor = new MenuAnchor(e.GetPosition(_root).X, e.GetPosition(_root).Y, null);
        MenuContext? context = null;
        if (VisualWalk.Up<MessageRowViewModel>(source) is { } row)
        {
            context = MailContext(MenuIds.Mail, row.Context);
        }
        else if (VisualWalk.Up<FolderRowViewModel>(source) is { } folder)
        {
            context = FolderContext(folder.Context);
        }
        else if (VisualWalk.Up<TagRowViewModel>(source) is { } tag)
        {
            context = Base(MenuIds.Tag) with { TagId = tag.Context.Id };
        }
        else if (HasAutomationIdAbove(source, "sidebar-unified"))
        {
            context = Base(MenuIds.UnifiedRow);
        }
        else if (HasAutomationIdAbove(source, "sidebar.folders.header"))
        {
            context = Base(MenuIds.FoldersHeader);
        }
        else if (IsInList(source))
        {
            context = Base(MenuIds.List);
        }

        if (context is null)
        {
            return;
        }

        e.Handled = true;
        _menus.Show(context, anchor);
    }

    private static bool HasAutomationIdAbove(DependencyObject? node, string id)
    {
        for (; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement && string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(node), id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInList(DependencyObject? node)
    {
        for (; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is MessageListView)
            {
                return true;
            }
        }

        return false;
    }

    private MessageRowViewModel? FindRow(string target)
    {
        var rows = _list.Messages.ToList();
        if (int.TryParse(target, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            return index < rows.Count ? rows[index] : null;
        }

        return rows.FirstOrDefault(r => string.Equals(r.Id, target, StringComparison.Ordinal) || r.Subject.Contains(target, StringComparison.OrdinalIgnoreCase));
    }

    private MenuAnchor AnchorOf(object viewModel)
    {
        if (_root is not null && VisualWalk.Find(_root, e => ReferenceEquals(e.DataContext, viewModel) && e.ActualWidth > 60 && e.ActualHeight > 16) is { } element)
        {
            var p = element.TransformToVisual(_root).TransformPoint(new Windows.Foundation.Point(element.ActualWidth * 0.5, element.ActualHeight * 0.5));
            return new MenuAnchor(p.X, p.Y, null);
        }

        return new MenuAnchor(120, 140, null);
    }

    private MenuContext Base(string menuId) => new(menuId, [], Mailbox(_list.MailboxId), null, null, null, CommandSurface.ContextMenu);

    private MenuContext FolderContext(FolderRowViewModel folder) => Base(MenuIds.Folder) with { Mailbox = Mailbox(folder.Id) };

    private MenuContext MailContext(string menuId, MessageRowViewModel row)
    {
        var target = Target(row);
        var selected = _selection.Current;
        var targets = selected.Count > 1 && selected.Any(t => string.Equals(t.EmailId, target.EmailId, StringComparison.Ordinal)) ? selected : [target];
        return Base(menuId) with { Targets = targets };
    }

    private MailTarget Target(MessageRowViewModel row)
    {
        if (_resolver?.Resolve(row) is { } resolved)
        {
            return resolved;
        }

        var key = _active.AccountKey ?? string.Empty;
        return new MailTarget(key, key, row.Id, row.Summary.ThreadId ?? row.Id, row.Key, row.Summary.MailboxIds, row.Summary.Keywords);
    }

    private MailboxRef? Mailbox(string? mailboxId)
    {
        if (mailboxId is null)
        {
            return null;
        }

        var key = _active.AccountKey ?? string.Empty;
        var role = _sidebar.Folders.FirstOrDefault(f => string.Equals(f.Id, mailboxId, StringComparison.Ordinal))?.Role ?? MailboxRole.None;
        return new MailboxRef(key, key, mailboxId, role == MailboxRole.None ? null : role.ToString().ToLowerInvariant());
    }
}
