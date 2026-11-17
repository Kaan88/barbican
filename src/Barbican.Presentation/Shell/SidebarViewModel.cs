// Mirrors: M1-plan §4.3 (Folders, Tags, sections, Storage); Rust mail.rs rebuild_folders :115, toggle_folder :207, tags.rs rows :140, glance.rs refresh
// (quota); Bulwark components/layout/sidebar.tsx; spec I-038..I-063, J-046. Owner mailcore.

using Barbican.Core.Mail.Glance;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Tags;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>The mail sidebar: folders, tags and the storage meter. Every refresh goes through ListSync, so unchanged rows are not touched.</summary>
public sealed class SidebarViewModel : ObservableObject, IDisposable, Actions.IMailboxDirectory
{
    private readonly MessageListViewModel _list;
    private readonly MailEventHub _hub;
    private readonly IPrefsStore _prefs;
    private readonly ISystemAccessibilitySource _system;
    private readonly IHookBus _hooks;
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedTags = new(StringComparer.Ordinal);
    private MailboxTree _tree = new([]);
    private string? _selectedFolderId;
    private string? _selectedTagId;
    private string? _lastFolderId;
    private string _storageText = string.Empty;
    private double _storageFraction = -1;
    private QuotaInfo? _quota;
    private bool _foldersExpanded = true;
    private bool _tagsExpanded = true;

    public SidebarViewModel(MessageListViewModel list, MailEventHub hub, IPrefsStore prefs, ISystemAccessibilitySource system, IHookBus hooks)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(hooks);
        _list = list;
        _hub = hub;
        _prefs = prefs;
        _system = system;
        _hooks = hooks;
        _hub.EventReceived += OnSyncEvent;
        _prefs.Changed += OnPrefsChanged;
        _list.PropertyChanged += OnListChanged;
        _list.InArchiveTree = InArchiveTree;
        RefreshTags();
    }

    /// <summary>The folders as last read (the action service finds the Trash, Archive and Junk folders in it).</summary>
    public MailboxTree Tree => _tree;

    public BulkObservableCollection<FolderRowViewModel> Folders { get; } = [];

    public BulkObservableCollection<TagRowViewModel> Tags { get; } = [];

    public SyncStats LastFolderSync { get; private set; }

    public string? SelectedFolderId
    {
        get => _selectedFolderId;
        private set => SetProperty(ref _selectedFolderId, value);
    }

    /// <summary>The tag the list is filtered to (its row is highlighted and no folder is), or null.</summary>
    public string? SelectedTagId
    {
        get => _selectedTagId;
        private set
        {
            if (SetProperty(ref _selectedTagId, value))
            {
                RefreshTags();
            }
        }
    }

    /// <summary>The list shows "All inboxes": no folder row is selected and the header keeps the unified title.</summary>
    public bool UnifiedOpen { get; private set; }

    /// <summary>The Inbox's unread count (the rail badge and the window title).</summary>
    public int InboxUnread => FolderRows.InboxUnread(_tree);

    /// <summary>"1.2 GB of 15 GB", or empty when the server reports no quota.</summary>
    public string StorageText
    {
        get => _storageText;
        private set => SetProperty(ref _storageText, value);
    }

    /// <summary>0..1, or -1 when there is no limit.</summary>
    public double StorageFraction
    {
        get => _storageFraction;
        private set => SetProperty(ref _storageFraction, value);
    }

    public bool FoldersExpanded
    {
        get => _foldersExpanded;
        set => SetProperty(ref _foldersExpanded, value);
    }

    public bool TagsExpanded
    {
        get => _tagsExpanded;
        set => SetProperty(ref _tagsExpanded, value);
    }

    /// <summary>The mailbox tree changed (loaded, pushed or an account switch).</summary>
    public void Update(MailboxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _tree = tree;
        RebuildFolders();
        OnPropertyChanged(nameof(InboxUnread));
        _hooks.Fire(HookNames.OnMailboxesRefresh, () => tree);
    }

    /// <summary>The storage quota (null when the server reports none).</summary>
    public void SetQuota(QuotaInfo? quota)
    {
        var changed = !Equals(_quota, quota);
        _quota = quota;
        StorageText = GlanceRules.QuotaText(quota);
        StorageFraction = GlanceRules.QuotaFraction(quota);
        if (changed && quota is not null)
        {
            _hooks.Fire(HookNames.OnQuotaChange, () => quota);
        }
    }

    /// <summary>Opens a folder in the list.</summary>
    public Task SelectFolderAsync(string folderId)
    {
        ArgumentException.ThrowIfNullOrEmpty(folderId);
        var mailbox = _tree.All.FirstOrDefault(m => string.Equals(m.Id, folderId, StringComparison.Ordinal));
        if (mailbox is null)
        {
            return Task.CompletedTask;
        }

        SelectedFolderId = folderId;
        _lastFolderId = folderId;
        return _list.OpenFolderAsync(folderId, mailbox.Role, FolderRows.DisplayName(mailbox));
    }

    /// <summary>Opens the folder with a role (the <c>g</c> sequences); false when the account has none.</summary>
    public bool GoToRole(MailboxRole role)
    {
        var mailbox = _tree.All.FirstOrDefault(m => m.Role == role);
        if (mailbox is null)
        {
            return false;
        }

        _ = SelectFolderAsync(mailbox.Id);
        return true;
    }

    /// <summary>A click on a tag row: filters the list to the tag, or, on the selected tag, goes back to the folder that was open (Bulwark sidebar.tsx:1181).</summary>
    public Task SelectTagAsync(string tagId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tagId);
        if (string.Equals(_selectedTagId, tagId, StringComparison.Ordinal))
        {
            return ClearTagAsync();
        }

        var label = Tags.FirstOrDefault(t => string.Equals(t.Id, tagId, StringComparison.Ordinal))?.Label
            ?? TagRules.Parse(_prefs.Current.Values["emailKeywords"]).FirstOrDefault(d => string.Equals(d.Id, tagId, StringComparison.Ordinal))?.Label
            ?? tagId;
        return _list.OpenTagAsync(tagId, label, [.. _tree.All.Where(m => m.Role is MailboxRole.Trash or MailboxRole.Junk).Select(m => m.Id)]);
    }

    /// <summary>Leaves the tag view: the folder that was open before comes back, else the Inbox.</summary>
    public Task ClearTagAsync()
    {
        if (_selectedTagId is null)
        {
            return Task.CompletedTask;
        }

        var back = _lastFolderId is { } id && _tree.All.Any(m => string.Equals(m.Id, id, StringComparison.Ordinal)) ? id : _tree.All.FirstOrDefault(m => m.Role == MailboxRole.Inbox)?.Id;
        return back is null ? Task.CompletedTask : SelectFolderAsync(back);
    }

    /// <summary>Folds or unfolds a folder's children.</summary>
    public void ToggleFolder(string id)
    {
        if (!_collapsedFolders.Remove(id))
        {
            _collapsedFolders.Add(id);
        }

        RebuildFolders();
    }

    /// <summary>ArrowRight unfolds and ArrowLeft folds the selected folder (Bulwark sidebar.tsx:2375); false when the key had nothing to do.</summary>
    public bool SetSelectedExpanded(bool expanded)
    {
        var row = _selectedFolderId is null ? null : Folders.FirstOrDefault(f => string.Equals(f.Id, _selectedFolderId, StringComparison.Ordinal));
        if (row is null || row.Chevron != (expanded ? 2 : 1))
        {
            return false;
        }

        ToggleFolder(row.Id);
        return true;
    }

    /// <summary>Folds or unfolds a tag's children.</summary>
    public void ToggleTag(string id)
    {
        if (!_collapsedTags.Remove(id))
        {
            _collapsedTags.Add(id);
        }

        RefreshTags();
    }

    /// <summary>Reads the tags and the nesting setting again (the settings changed, or a tag was edited).</summary>
    public void RefreshTags()
    {
        var prefs = _prefs.Current;
        var defs = TagRules.Parse(prefs.Values["emailKeywords"]);
        var nested = prefs.Bool("nestedTags", false);
        var cvd = A11y.From(prefs, _system.Current).Cvd;
        var rows = TagRules.Rows(defs, nested, _collapsedTags)
            .Select(r =>
            {
                var rgb = TagRules.ColorRgb(r.Def.Color);
                return new TagRowViewModel(r.Def.Id, r.Def.Label, cvd ? A11y.CvdColor(rgb) : rgb, r.Depth, r.Chevron, string.Equals(r.Def.Id, _selectedTagId, StringComparison.Ordinal));
            })
            .ToList();
        ListSync.SyncModel(Tags, rows, r => r.Id);
    }

    public void Dispose()
    {
        _hub.EventReceived -= OnSyncEvent;
        _prefs.Changed -= OnPrefsChanged;
        _list.PropertyChanged -= OnListChanged;
    }

    /// <summary>True when the folder is the Archive or lies below it and an Inbox exists (Bulwark lib/archive-inbox.ts:10-28).</summary>
    private bool InArchiveTree(string folderId)
    {
        var byId = _tree.All.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var at = byId.GetValueOrDefault(folderId); at is not null && seen.Add(at.Id); at = at.ParentId is { } parent ? byId.GetValueOrDefault(parent) : null)
        {
            if (at.Role == MailboxRole.Archive)
            {
                return _tree.All.Any(m => m.Role == MailboxRole.Inbox);
            }
        }

        return false;
    }

    // The list is the one truth for the open folder: when something other than a folder row opened it (the unified inbox, a command, the
    // palette), the highlight follows. "All inboxes" is no folder of the tree, so no folder row stays highlighted (unified review request).
    private void OnListChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(MessageListViewModel.TagId), StringComparison.Ordinal))
        {
            // the tag view highlights its tag and no folder (Bulwark sidebar.tsx:1181, selectedKeyword); leaving it brings the folder back through MailboxId
            SelectedTagId = _list.TagId;
            if (_list.TagId is not null)
            {
                UnifiedOpen = false;
                SelectedFolderId = null;
            }

            return;
        }

        if (!string.Equals(e.PropertyName, nameof(MessageListViewModel.MailboxId), StringComparison.Ordinal))
        {
            return;
        }

        var id = _list.MailboxId;
        if (string.Equals(id, Core.Mail.Unified.UnifiedRows.FolderId, StringComparison.Ordinal))
        {
            UnifiedOpen = true;
            SelectedFolderId = null;
        }
        else if (id is not null && _tree.All.Any(m => string.Equals(m.Id, id, StringComparison.Ordinal)))
        {
            UnifiedOpen = false;
            _lastFolderId = id;
            SelectedFolderId = id;
        }
    }

    private void RebuildFolders()
    {
        var options = FolderRowOptions.Read(_prefs.Current, _system.Current);
        var rows = FolderRows.Build(_tree, _collapsedFolders, options);
        LastFolderSync = ListSync.SyncModel(Folders, rows, r => r.Id);
    }

    private void OnSyncEvent(object? sender, MailSyncEventArgs args)
    {
        if (args.Event is MailboxesChangedEvent changed)
        {
            Update(changed.Tree);
        }
    }

    private void OnPrefsChanged(object? sender, EventArgs e)
    {
        RebuildFolders();
        RefreshTags();
    }
}
