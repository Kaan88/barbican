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
public sealed class SidebarViewModel : ObservableObject, IDisposable
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
        RefreshTags();
    }

    public BulkObservableCollection<FolderRowViewModel> Folders { get; } = [];

    public BulkObservableCollection<TagRowViewModel> Tags { get; } = [];

    public SyncStats LastFolderSync { get; private set; }

    public string? SelectedFolderId
    {
        get => _selectedFolderId;
        private set => SetProperty(ref _selectedFolderId, value);
    }

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

    /// <summary>Folds or unfolds a folder's children.</summary>
    public void ToggleFolder(string id)
    {
        if (!_collapsedFolders.Remove(id))
        {
            _collapsedFolders.Add(id);
        }

        RebuildFolders();
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
                return new TagRowViewModel(r.Def.Id, r.Def.Label, cvd ? A11y.CvdColor(rgb) : rgb, r.Depth, r.Chevron);
            })
            .ToList();
        ListSync.SyncModel(Tags, rows, r => r.Id);
    }

    public void Dispose()
    {
        _hub.EventReceived -= OnSyncEvent;
        _prefs.Changed -= OnPrefsChanged;
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
