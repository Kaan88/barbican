// Mirrors: Bulwark components/settings/folder-settings.tsx and keyword-settings.tsx (the folder tree with a panel for the picked folder: new subfolder, rename, delete asks, role, subscribe, icon, count mode, pin, mute, mark tree read, move under; the tag manager: add, rename, colour, delete asks), Rust folders and tags; I-430, I-431.
// The tree is SrvUiFolderTreeRules; moving is a stepping button over the allowed parents (no drag, no drop-down: ADR 0007). Owner srv-ui.

using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The Folders and tags settings page.</summary>
internal sealed class SrvUiOrganizePage : SrvUiPage
{
    private static readonly string[] Icons = ["", "inbox", "star", "folder", "archive", "tag", "heart", "briefcase"];
    private static readonly string[] Modes = ["unread", "all", "none"];
    private readonly SrvUiFoldersViewModel _folders;
    private readonly SrvUiTagsViewModel _tags;
    private string _newName = string.Empty;
    private string _renameTo = string.Empty;
    private string _newTag = string.Empty;
    private string _renameTag = string.Empty;
    private string? _movedFor;
    private long _accountGeneration;

    public SrvUiOrganizePage(SrvUiFoldersViewModel folders, SrvUiTagsViewModel tags)
        : base(SettingsPageIds.Organize, "Folders and tags", "Arrange your folders and set how each one behaves, and manage the tags you put on messages.")
    {
        _folders = folders;
        _tags = tags;
        _accountGeneration = folders.AccountGeneration;
        _folders.PropertyChanged += (_, _) =>
        {
            if (_accountGeneration != _folders.AccountGeneration)
            {
                _accountGeneration = _folders.AccountGeneration;
                _newName = string.Empty;
                _renameTo = string.Empty;
                _movedFor = null;
            }

            Invalidate();
        };
        _tags.PropertyChanged += (_, _) => Invalidate();
    }

    protected override Task LoadAsync()
    {
        _tags.Refresh();
        return _folders.LoadAsync(CancellationToken.None);
    }

    protected override void Render()
    {
        Reset();
        var folders = SrvUiKit.Section("Folders");
        AutomationProperties.SetAutomationId(folders, "srv.organize.folders");
        Body.Children.Add(folders);
        var tree = new StackPanel { Spacing = 2 };
        foreach (var row in _folders.Rows)
        {
            tree.Children.Add(FolderLine(row));
        }

        Body.Children.Add(SrvUiKit.Card(tree, "srv.organize.tree"));
        Body.Children.Add(FolderPanel());
        var tags = SrvUiKit.Section("Tags");
        AutomationProperties.SetAutomationId(tags, "tags");
        Body.Children.Add(tags);
        Body.Children.Add(TagPanel());
    }

    private Grid FolderLine(SrvUiFolderRow row)
    {
        var node = row.Node;
        var generation = _folders.AccountGeneration;
        var picked = string.Equals(node.Id, _folders.SelectedId, StringComparison.Ordinal);
        var grid = new Grid { Margin = new Thickness(row.Depth * 20, 0, 0, 0), ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (row.HasChildren)
        {
            var chevron = SrvUiKit.Small(row.Collapsed ? "▸" : "▾", "srv.folder.toggle." + node.Id, () => _folders.ToggleCollapsed(node.Id));
            chevron.Padding = new Thickness(0);
            chevron.MinWidth = 24;
            grid.Children.Add(chevron);
        }

        var name = node.Name + (_folders.Subscribed(node.Id) ? string.Empty : "  (not subscribed)");
        var unread = _folders.Unread(node.Id);
        var button = new Button
        {
            Content = name + (unread > 0 ? "   " + unread : string.Empty) + (string.IsNullOrEmpty(node.Role) ? string.Empty : "   · " + node.Role),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = 30,
            FontSize = 13,
            Style = (Style)Application.Current.Resources[picked ? "Button.Primary" : "Button.Action"],
        };
        button.Click += (_, _) => { if (_folders.IsCurrentGeneration(generation)) _folders.Select(node.Id); };
        AutomationProperties.SetAutomationId(button, "srv.folder.row." + node.Id);
        AutomationProperties.SetName(button, node.Name);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private Border FolderPanel()
    {
        var panel = new StackPanel { Spacing = 6 };
        var selected = _folders.Selected;
        var generation = _folders.AccountGeneration;
        var selectedId = selected?.Id;
        panel.Children.Add(Label("New folder"));
        panel.Children.Add(SrvUiKit.Row(
            Text("srv.folder.new-name", _newName, v => _newName = v, placeholder: "Folder name"),
            SrvUiKit.Small("Create at top", "srv.folder.create", () => SrvUiKit.Run(() => CreateAsync(false, generation, selectedId))),
            Enable(SrvUiKit.Small("Create inside selected", "srv.folder.create-inside", () => SrvUiKit.Run(() => CreateAsync(true, generation, selectedId))), selected is not null)));
        if (selected is null)
        {
            panel.Children.Add(SrvUiKit.Dim("Pick a folder above to rename it, give it a role or change how it counts mail."));
        }
        else
        {
            AddSelected(panel, selected);
        }

        panel.Children.Add(SrvUiKit.Error(_folders.Error));
        return SrvUiKit.Card(panel, "srv.folder.panel");
    }

    private void AddSelected(StackPanel panel, FolderAdminNode f)
    {
        var id = f.Id;
        var generation = _folders.AccountGeneration;
        bool Current() => _folders.IsCurrentGeneration(generation) && string.Equals(id, _folders.SelectedId, StringComparison.Ordinal);
        void RunSelected(Func<Task> action) { if (Current()) SrvUiKit.Run(action); }
        panel.Children.Add(SrvUiKit.Strong(f.Name));
        panel.Children.Add(SrvUiKit.Row(
            Text("srv.folder.rename-to", _renameTo.Length == 0 ? f.Name : _renameTo, v => { if (Current()) _renameTo = v; }),
            SrvUiKit.Small("Rename", "srv.folder.rename", () => RunSelected(async () =>
            {
                if (await _folders.RenameAsync(_renameTo.Length == 0 ? f.Name : _renameTo, CancellationToken.None).ConfigureAwait(true))
                {
                    _renameTo = string.Empty;
                }
            }))));
        var roles = new List<string> { string.Empty };
        roles.AddRange(FolderAdminRulesRoles());
        panel.Children.Add(Label("Role (a role belongs to one folder; giving it here takes it from the other)"));
        panel.Children.Add(SrvUiKit.Cycle("srv.folder.role", [.. roles.Select(r => r.Length == 0 ? "No role" : r)], Math.Max(0, roles.IndexOf(f.Role ?? string.Empty)), i => RunSelected(() => _folders.SetRoleAsync(roles[i].Length == 0 ? null : roles[i], CancellationToken.None))));
        var subscribed = _folders.Subscribed(id);
        panel.Children.Add(Check("srv.folder.subscribed", "Subscribed", subscribed, v => RunSelected(() => _folders.SubscribeAsync(v, CancellationToken.None))));
        panel.Children.Add(Label("Icon"));
        panel.Children.Add(SrvUiKit.Cycle("srv.folder.icon", [.. Icons.Select(i => i.Length == 0 ? "Default" : i)], Math.Max(0, Array.IndexOf(Icons, _folders.IconOf(id))), i => RunSelected(() => _folders.SetPrefAsync(FolderAdminPref.Icon, Icons[i], CancellationToken.None))));
        panel.Children.Add(Label("Count"));
        panel.Children.Add(SrvUiKit.Cycle("srv.folder.count", ["Unread", "All messages", "No count"], Math.Max(0, Array.IndexOf(Modes, _folders.CountModeOf(id))), i => RunSelected(() => _folders.SetPrefAsync(FolderAdminPref.CountMode, Modes[i], CancellationToken.None))));
        panel.Children.Add(Check("srv.folder.pin", "Pin to the top of the sidebar", _folders.Prefs.Pinned.Contains(id), v => RunSelected(() => _folders.SetPrefAsync(FolderAdminPref.Pin, v ? "true" : "false", CancellationToken.None))));
        panel.Children.Add(Check("srv.folder.mute", "Mute notifications", _folders.Prefs.Muted.Contains(id), v => RunSelected(() => _folders.SetPrefAsync(FolderAdminPref.Mute, v ? "true" : "false", CancellationToken.None))));
        var targets = _folders.MoveTargets();
        var moveRow = SrvUiKit.Row();
        if (!string.Equals(_movedFor, id, StringComparison.Ordinal))
        {
            _movedFor = id;
        }

        moveRow.Children.Add(SrvUiKit.Small("Move to top", "srv.folder.move-top", () => RunSelected(() => _folders.MoveUnderAsync(null, CancellationToken.None)), false));
        if (targets.Count > 0)
        {
            var chosen = targets[0].Id;
            moveRow.Children.Add(SrvUiKit.Cycle("srv.folder.move-to", [.. targets.Select(t => t.Name)], 0, i => chosen = targets[i].Id));
            moveRow.Children.Add(SrvUiKit.Small("Move under", "srv.folder.move", () => RunSelected(() => _folders.MoveUnderAsync(chosen, CancellationToken.None))));
        }

        panel.Children.Add(moveRow);
        panel.Children.Add(SrvUiKit.Row(
            SrvUiKit.Small("Mark folder and subfolders read", "srv.folder.mark-read", () => RunSelected(() => _folders.MarkTreeReadAsync(CancellationToken.None))),
            SrvUiKit.Small("Delete folder", "srv.folder.delete", () => RunSelected(() => DeleteFolderAsync(f, generation)), true)));
    }

    private static IEnumerable<string> FolderAdminRulesRoles() => FolderAdminRules.AssignableRoles;

    private async Task CreateAsync(bool inside, long generation, string? selectedId)
    {
        if (!_folders.IsCurrentGeneration(generation) || (inside && !string.Equals(selectedId, _folders.SelectedId, StringComparison.Ordinal))) return;
        if (await _folders.CreateAsync(_newName, inside, CancellationToken.None).ConfigureAwait(true))
        {
            _newName = string.Empty;
            Invalidate();
        }
    }

    private async Task DeleteFolderAsync(FolderAdminNode f, long generation)
    {
        if (await SrvUiKit.AskAsync("Delete folder", "Delete the folder \"" + f.Name + "\" and every message in it? This cannot be undone.", "Delete").ConfigureAwait(true) && _folders.IsCurrentGeneration(generation) && string.Equals(f.Id, _folders.SelectedId, StringComparison.Ordinal))
        {
            _ = await _folders.DeleteAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    private Border TagPanel()
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var row in _tags.Rows)
        {
            var def = row.Def;
            var picked = string.Equals(def.Id, _tags.SelectedId, StringComparison.Ordinal);
            var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Rgb(TagRgb(def.Color))), VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = def.Label, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 28, FontSize = 13, Style = (Style)Application.Current.Resources[picked ? "Button.Primary" : "Button.Action"] };
            button.Click += (_, _) => _tags.Select(def.Id);
            AutomationProperties.SetAutomationId(button, "srv.tag.row." + def.Id);
            AutomationProperties.SetName(button, def.Label);
            var line = SrvUiKit.Row(dot, button);
            line.Margin = new Thickness(row.Depth * 20, 0, 0, 0);
            panel.Children.Add(line);
        }

        if (_tags.Rows.Count == 0)
        {
            panel.Children.Add(SrvUiKit.Dim("No tags yet."));
        }

        panel.Children.Add(SrvUiKit.Row(
            Text("srv.tag.new-name", _newTag, v => _newTag = v, placeholder: "Tag name"),
            SrvUiKit.Small("Add tag", "srv.tag.add", () => AddTag(false)),
            Enable(SrvUiKit.Small("Add inside selected", "srv.tag.add-inside", () => AddTag(true)), _tags.SelectedId is not null)));
        if (_tags.SelectedId is { } sel)
        {
            panel.Children.Add(SrvUiKit.Row(
                Text("srv.tag.rename-to", _renameTag, v => _renameTag = v, placeholder: "New name"),
                SrvUiKit.Small("Rename", "srv.tag.rename", () =>
                {
                    if (_tags.Rename(_renameTag))
                    {
                        _renameTag = string.Empty;
                    }
                })));
            var palette = SrvUiTagsViewModel.Palette;
            panel.Children.Add(Label("Colour"));
            panel.Children.Add(SrvUiKit.Cycle("srv.tag.colour", [.. palette], Math.Max(0, palette.ToList().IndexOf(_tags.ColourOf(sel) ?? string.Empty)), i => _tags.SetColour(palette[i])));
            panel.Children.Add(SrvUiKit.Small("Delete tag", "srv.tag.delete", () => SrvUiKit.Run(DeleteTagAsync), true));
        }

        panel.Children.Add(SrvUiKit.Error(_tags.Error));
        return SrvUiKit.Card(panel, "srv.tag.panel");
    }

    private static uint TagRgb(string colour) => Barbican.Core.Mail.Tags.TagRules.ColorRgb(colour);

    private static Windows.UI.Color Rgb(uint rgb) => Windows.UI.Color.FromArgb(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));

    private void AddTag(bool inside)
    {
        if (_tags.Add(_newTag, inside))
        {
            _newTag = string.Empty;
        }
    }

    private async Task DeleteTagAsync()
    {
        if (await SrvUiKit.AskAsync("Delete tag", "Delete this tag? Messages that have it keep it, it just stops being listed.", "Delete").ConfigureAwait(true))
        {
            _ = _tags.Delete();
        }
    }

    private static Button Enable(Button button, bool on)
    {
        button.IsEnabled = on;
        return button;
    }
}
