// Mirrors: Rust ctxmenu.rs submenu "move" :575 and mail.rs rebuild_folders :115 (the folder list a menu reads); Bulwark components/layout/mailbox-context-menu.tsx (myRights). The
// mailbox tree of the active account is kept as it arrives (the start-up load, then the sync events), so a menu reads it when it opens without a request. Owner menus.

using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Tags;
using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Menus;

/// <summary>The folders, tags and settings of the active account, for the menu providers.</summary>
public sealed class MailboxMenuData : IMenuDataSource, IStartupTask, IDisposable
{
    private readonly IActiveMail _active;
    private readonly MailEventHub _hub;
    private readonly IPrefsStore _prefs;
    private readonly ISystemAccessibilitySource _system;
    private readonly Barbican.Presentation.Reader.IPhishingReportSetups? _phishing;
    private MailboxTree _tree = new([]);
    private string? _treeAccount;

    public MailboxMenuData(IActiveMail active, MailEventHub hub, IPrefsStore prefs, ISystemAccessibilitySource system, Barbican.Presentation.Reader.IPhishingReportSetups? phishing = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(system);
        _active = active;
        _hub = hub;
        _prefs = prefs;
        _system = system;
        _phishing = phishing;
    }

    /// <summary>Whether the open message is plain text; the reader view sets it when it shows a message (default: HTML).</summary>
    public bool BodyIsPlainText { get; set; }

    /// <inheritdoc />
    public bool CanReportPhishing(string? accountKey) => accountKey is not null && _phishing?.Cached(accountKey).IsOn == true;

    /// <inheritdoc />
    public bool NestedTags => _prefs.Current.Bool("nestedTags", false);

    /// <inheritdoc />
    public bool ConfirmBulkDelete => _prefs.Current.Bool("confirmBulkDelete", true);

    /// <inheritdoc />
    public bool GroupListByDate => _prefs.Current.Bool("groupListByDate", false);

    /// <inheritdoc />
    public string? ListSortKey(MailboxRole role)
    {
        // the stored order applies to the Inbox only unless "apply to all folders" is on (ListSortResolver / MessageListViewModel.ConfiguredSort)
        var prefs = _prefs.Current;
        var applies = string.Equals(prefs.Str("messageListOrderScope", "inbox"), "all", StringComparison.Ordinal) || role == MailboxRole.Inbox;
        return applies ? ListSortChoices.KeyFor(MessageListOrder.Parse(prefs.Values["messageListOrder"])) : ListSortChoices.NewestKey;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hub.EventReceived += OnEvent;
        _active.Changed += OnAccountChanged;
        return LoadAsync(cancellationToken);
    }

    public void Dispose()
    {
        _hub.EventReceived -= OnEvent;
        _active.Changed -= OnAccountChanged;
    }

    /// <summary>Hands the menus a mailbox tree directly (tests, and the headless sample run).</summary>
    public void SetTree(string? accountKey, MailboxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _tree = tree;
        _treeAccount = accountKey;
    }

    /// <inheritdoc />
    public IReadOnlyList<MenuFolder> Folders(string? accountKey)
    {
        if (accountKey is not null && _treeAccount is not null && !string.Equals(accountKey, _treeAccount, StringComparison.Ordinal))
        {
            return [];
        }

        var byId = _tree.All.ToDictionary(m => m.Id, StringComparer.Ordinal);
        return FolderRows.Build(_tree, new HashSet<string>(StringComparer.Ordinal), new FolderRowOptions())
            .Select(r => new MenuFolder(r.Id, r.Name, r.Role, r.Depth, byId.TryGetValue(r.Id, out var m) ? MailboxRights.From(m) : MailboxRights.Full) { Unread = r.Unread, Total = r.Total })
            .ToList();
    }

    /// <inheritdoc />
    public MenuFolder? Folder(string? accountKey, string? mailboxId) =>
        mailboxId is null ? null : Folders(accountKey).FirstOrDefault(f => string.Equals(f.Id, mailboxId, StringComparison.Ordinal));

    /// <inheritdoc />
    public IReadOnlyList<MenuTag> Tags()
    {
        var prefs = _prefs.Current;
        var defs = TagRules.Parse(prefs.Values["emailKeywords"]);
        var cvd = A11y.From(prefs, _system.Current).Cvd;
        return TagRules.Rows(defs, NestedTags, new HashSet<string>(StringComparer.Ordinal))
            .Select(r =>
            {
                var rgb = TagRules.ColorRgb(r.Def.Color);
                return new MenuTag(r.Def.Id, r.Def.Label, r.Def.Color, cvd ? A11y.CvdColor(rgb) : rgb);
            })
            .ToList();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var mail = _active.Mail;
        if (mail is null)
        {
            return;
        }

        var key = _active.AccountKey;
        var result = await mail.GetMailboxesAsync(ct).ConfigureAwait(true);
        if (result.IsOk && result.Value is { } tree && string.Equals(key, _active.AccountKey, StringComparison.Ordinal))
        {
            SetTree(key, tree);
        }
    }

    private void OnEvent(object? sender, MailSyncEventArgs e)
    {
        if (e.Event is MailboxesChangedEvent changed && string.Equals(e.AccountKey, _active.AccountKey, StringComparison.Ordinal))
        {
            SetTree(e.AccountKey, changed.Tree);
        }
    }

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        _tree = new MailboxTree([]);
        _treeAccount = null;
        _ = LoadAsync(CancellationToken.None);
    }
}
