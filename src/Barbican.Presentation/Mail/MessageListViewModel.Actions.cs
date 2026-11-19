// Mirrors: Rust mail.rs toggle_check :829, select_all :837, next_after :871, patch_local :887, remove_local :901; Bulwark components/email/email-list.tsx
// (Ctrl-click toggles, Shift-click and Shift+Arrow extend a range, the row tick, the "Select all N" banner), components/email/selection-view.tsx
// (count text), stores/email-store.ts (optimistic updates and rollback); spec I-120, I-123, I-124, I-126, I-138..I-142. Owner actions.
//
// The list is edited in place: rows leave and return through ListSync (no reset), and the checked set is re-applied to the rows after every rebuild.

using System.Globalization;
using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Mail;

public sealed partial class MessageListViewModel : IListEditor, IMailSelection
{
    /// <summary>"Select all in folder" resolves at most this many messages; the confirmation says so when the cap is hit (decision M2-Q5).</summary>
    public const int AllInFolderCap = 10_000;

    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private string? _anchorId;
    private string? _cursorId;
    private bool _allInFolder;
    private string? _hoverId;

    /// <summary>The selection changed (ticks, the open message): the selection bar, the menus and the palette read <see cref="Current"/> again.</summary>
    public event EventHandler? Changed;

    /// <summary>The open folder's role.</summary>
    public MailboxRole Role => _role;

    /// <summary>How many messages are ticked (all of the folder's when <see cref="AllInFolder"/>).</summary>
    public int CheckedCount => _allInFolder ? Math.Max(Total, _checked.Count) : _checked.Count;

    /// <summary>At least one tick: the selection bar replaces the list header.</summary>
    public bool HasChecked => _checked.Count > 0;

    /// <summary>"Select all N in folder" was taken: acting resolves the ids with Email/query (I-142).</summary>
    public bool AllInFolder => _allInFolder;

    public bool ShowsConversations => _query?.CollapseThreads == true;

    /// <summary>"N selected · M unread" (selection-view.tsx).</summary>
    public string SelectionLabel
    {
        get
        {
            var unread = Messages.Count(r => _checked.Contains(r.Id) && r.Unread);
            return string.Create(CultureInfo.InvariantCulture, $"{CheckedCount} selected · {unread} unread");
        }
    }

    /// <summary>The tri-state of the select-all toggle: 0 nothing ticked, 1 some, 2 every loaded message.</summary>
    public int SelectAllState
    {
        get
        {
            var loaded = Messages.Count();
            return _checked.Count == 0 ? 0 : _checked.Count >= loaded ? 2 : 1;
        }
    }

    /// <summary>Every loaded message is ticked and more follow on the server: the banner offers all of them (I-142).</summary>
    public bool CanSelectAllInFolder => SelectAllState == 2 && !_allInFolder && (CanLoadMore || (ShowsConversations && Messages.Any(r => r.ThreadCount > 1)));

    /// <summary>The ticked messages, or else the open one; empty when neither.</summary>
    public IReadOnlyList<MailTarget> Current => TargetsOf(_checked.Count > 0 ? Messages.Where(r => _checked.Contains(r.Id)) : Messages.Where(r => string.Equals(r.Id, _selectedId, StringComparison.Ordinal)));

    /// <summary>Tells whether a folder is the Archive or below it; the sidebar sets it (it holds the folder tree). Without it only the Archive folder itself counts.</summary>
    public Func<string, bool>? InArchiveTree { get; set; }

    /// <summary>The folder on screen is the Archive or below it: "Archive" reads "Move to Inbox" (Bulwark lib/archive-inbox.ts:10-28).</summary>
    public bool ArchiveMovesToInbox => _role == MailboxRole.Archive || (MailboxId is { } id && InArchiveTree?.Invoke(id) == true);

    /// <summary>The id of the row whose hover strip shows, or null (the automation `hover-sim` and the pointer set it; real hover is a hand test).</summary>
    public string? HoverId => _hoverId;

    /// <summary>The pointer or keyboard focus entered (or left, with null) a row: only that row shows its strip.</summary>
    public void SetHover(string? emailId)
    {
        if (string.Equals(_hoverId, emailId, StringComparison.Ordinal))
        {
            return;
        }

        _hoverId = emailId;
        ApplyChecks();
        OnPropertyChanged(nameof(HoverId));
    }

    /// <summary>Ctrl-click and the row tick: flips one message and makes it the anchor of the next Shift range.</summary>
    public void ToggleCheck(MessageRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _allInFolder = false;
        if (!_checked.Remove(row.Id))
        {
            _checked.Add(row.Id);
        }

        _anchorId = row.Id;
        _cursorId = row.Id;
        SelectionTouched();
    }

    /// <summary>Shift-click: ticks the range from the anchor to <paramref name="row"/> (date headers between them are skipped).</summary>
    public void ExtendTo(MessageRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rows = Messages.ToList();
        var to = IndexOf(rows, row.Id);
        if (to < 0)
        {
            return;
        }

        var from = IndexOf(rows, _anchorId ?? _selectedId ?? row.Id);
        if (from < 0)
        {
            from = to;
        }

        _allInFolder = false;
        _checked.Clear();
        for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            _checked.Add(rows[i].Id);
        }

        _anchorId = rows[from].Id;
        _cursorId = row.Id;
        SelectionTouched();
    }

    /// <summary>Shift+Down / Shift+Up (Shift+J / Shift+K): moves the range's free end by <paramref name="delta"/> rows without opening anything.</summary>
    public bool ExtendBy(int delta)
    {
        var rows = Messages.ToList();
        if (rows.Count == 0 || delta == 0)
        {
            return false;
        }

        var from = IndexOf(rows, _cursorId ?? _selectedId ?? string.Empty);
        if (from < 0)
        {
            from = delta > 0 ? -1 : rows.Count;
        }

        var to = Math.Clamp(from + delta, 0, rows.Count - 1);
        _anchorId ??= _selectedId ?? rows[Math.Clamp(from, 0, rows.Count - 1)].Id;
        ExtendTo(rows[to]);
        if (CanLoadMore && to >= rows.Count - 3)
        {
            _ = LoadMoreAsync();
        }

        return true;
    }

    /// <summary>Ticks every loaded message (Ctrl+A).</summary>
    public void SelectAllLoaded()
    {
        _allInFolder = false;
        _checked.Clear();
        foreach (var row in Messages)
        {
            _checked.Add(row.Id);
        }

        _anchorId = Messages.FirstOrDefault()?.Id;
        SelectionTouched();
    }

    /// <summary>The select-all toggle: nothing or some ticked selects every loaded message; all ticked clears.</summary>
    public void ToggleSelectAll()
    {
        if (SelectAllState == 2)
        {
            ClearChecked();
        }
        else
        {
            SelectAllLoaded();
        }
    }

    /// <summary>The banner's "Select all N in folder": acting on it resolves the ids with Email/query.</summary>
    public void SelectAllInFolder()
    {
        if (CanSelectAllInFolder)
        {
            _allInFolder = true;
            SelectionTouched();
        }
    }

    /// <summary>Clears every tick (Escape, the bar's clear button).</summary>
    public void ClearChecked()
    {
        if (_checked.Count == 0 && !_allInFolder)
        {
            return;
        }

        _checked.Clear();
        _allInFolder = false;
        _anchorId = null;
        _cursorId = null;
        SelectionTouched();
    }

    /// <summary>The targets of the selection; with "all in folder" the ids come from Email/query pages, not from what is loaded.</summary>
    public async Task<IReadOnlyList<MailTarget>> ResolveTargetsAsync(CancellationToken ct)
    {
        var mail = _active.Mail;
        if (!_allInFolder || _query is null || mail is null)
        {
            return Current;
        }

        var generation = _generation;
        var query = _query with { CollapseThreads = false };
        var account = _active.AccountKey;
        if (account is null) return [];
        // Enumerate the whole folder independently of the visible, collapsed cursor.
        using var unified = _unified is not null && string.Equals(query.MailboxId, UnifiedRows.FolderId, StringComparison.Ordinal)
            ? new UnifiedListSource(_unified.Sources) : null;
        IListSource source = (IListSource?)unified ?? _sources?.Resolve(query) ?? new MailServiceListSource(mail);
        var all = new List<EmailSummary>();
        ListCursor? after = null;
        do
        {
            var page = await source.QueryAsync(query, after, ct).ConfigureAwait(true);
            if (!page.IsOk || page.Value is null || !_allInFolder || generation != _generation || !string.Equals(account, _active.AccountKey, StringComparison.Ordinal))
            {
                return [];
            }

            all.AddRange(page.Value.Items.Take(AllInFolderCap - all.Count));
            after = page.Value.Next;
        }
        while (after is not null && all.Count < AllInFolderCap);
        return [.. all.Select(Target)];
    }

    /// <inheritdoc />
    public EmailSummary? Find(string emailId) => _loaded.Emails.FirstOrDefault(e => string.Equals(e.Id, emailId, StringComparison.Ordinal));

    /// <inheritdoc />
    public ListEdit RemoveRows(IReadOnlyCollection<string> emailIds)
    {
        ArgumentNullException.ThrowIfNull(emailIds);
        var openId = _selectedId;
        var openGoes = openId is not null && emailIds.Contains(openId, StringComparer.Ordinal);
        var next = NextAfter(openId ?? _cursorId, emailIds);
        var removed = _loaded.Remove(emailIds);
        if (removed.Count == 0)
        {
            return new ListEdit([], null);
        }

        foreach (var id in emailIds)
        {
            _checked.Remove(id);
        }

        _allInFolder = false;
        RebuildRows();
        Total = _loaded.Total;
        if (openGoes)
        {
            _opener.Close();
            if (!_prefs.Current.Bool("returnToListAfterAction", true) && next is not null && Messages.FirstOrDefault(r => string.Equals(r.Id, next, StringComparison.Ordinal)) is { } row)
            {
                Select(row, OpenReason.Key, ListDirection.Down);
            }
        }

        SelectionTouched();
        return new ListEdit(removed, next) { OpenId = openGoes ? openId : null, View = _query };
    }

    /// <inheritdoc />
    public void RestoreRows(ListEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.IsEmpty || (edit.View is not null && !Equals(edit.View, _query)))
        {
            // another list is on screen now: its rows are not these (the restored folder's push delta brings them back there)
            return;
        }

        _loaded.Restore(edit.Removed);
        RebuildRows();
        Total = _loaded.Total;
        SelectionTouched();
    }

    /// <inheritdoc />
    public void Reopen(ListEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.OpenId is not { } emailId || (edit.View is not null && !Equals(edit.View, _query))
            || (_selectedId is not null && !string.Equals(_selectedId, edit.NextId, StringComparison.Ordinal)))
        {
            // nothing was open, another list is on screen, or the user opened something else than what the action opened: leave it
            return;
        }

        if (Messages.FirstOrDefault(r => string.Equals(r.Id, emailId, StringComparison.Ordinal)) is { } row)
        {
            Select(row, OpenReason.Key, ListDirection.Down);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<EmailSummary> PatchRows(IReadOnlyCollection<string> emailIds, Func<EmailSummary, EmailSummary> change)
    {
        var before = _loaded.Patch(emailIds, change);
        if (before.Count > 0)
        {
            RebuildRows();
        }

        return before;
    }

    /// <inheritdoc />
    public void PutRows(IReadOnlyList<EmailSummary> before)
    {
        _loaded.Put(before);
        RebuildRows();
    }

    /// <summary>The target of one row (the hover strip, a drag, a menu on that row).</summary>
    public MailTarget TargetOf(MessageRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Target(row.Summary);
    }

    private static int IndexOf(List<MessageRowViewModel> rows, string id) => rows.FindIndex(r => string.Equals(r.Id, id, StringComparison.Ordinal));

    /// <summary>The message after <paramref name="current"/> that is not among <paramref name="ids"/>, else the one before it (Rust next_after :871).</summary>
    private string? NextAfter(string? current, IReadOnlyCollection<string> ids)
    {
        var rows = Messages.Select(r => r.Id).ToList();
        var at = current is null ? -1 : rows.FindIndex(id => string.Equals(id, current, StringComparison.Ordinal));
        if (at < 0)
        {
            return null;
        }

        bool Keep(string id) => !ids.Contains(id, StringComparer.Ordinal);
        return rows.Skip(at + 1).FirstOrDefault(Keep) ?? rows.Take(at).Reverse().FirstOrDefault(Keep);
    }

    private IReadOnlyList<MailTarget> TargetsOf(IEnumerable<MessageRowViewModel> rows) => [.. rows.Select(r => Target(r.Summary))];

    private MailTarget Target(EmailSummary e) =>
        new(_active.AccountKey ?? string.Empty, string.Empty, e.Id, e.ThreadId ?? string.Empty, e.Id, e.MailboxIds, e.Keywords);

    /// <summary>Re-applies the checked set to the row instances (a replaced row is a new object).</summary>
    private void ApplyChecks()
    {
        foreach (var row in Messages)
        {
            var on = _checked.Contains(row.Id);
            if (row.IsChecked != on)
            {
                row.IsChecked = on;
            }

            var hover = string.Equals(row.Id, _hoverId, StringComparison.Ordinal);
            if (row.IsHovered != hover)
            {
                if (hover)
                {
                    // the buttons are worked out once, when the strip is about to show: the message state and the folder decide them (I-132..I-135)
                    row.Strip = HoverStrip.For(row.Unread, row.Flagged, _role, ArchiveMovesToInbox, HoverStrip.Configured(_prefs.Current.Values[HoverStrip.SettingKey]));
                }

                row.IsHovered = hover;
            }
        }
    }

    private void SelectionTouched()
    {
        ApplyChecks();
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(AllInFolder));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(SelectAllState));
        OnPropertyChanged(nameof(CanSelectAllInFolder));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SelectedTouched()
    {
        if (_checked.Count == 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
