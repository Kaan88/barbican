// Mirrors: Rust unified.rs:242 users and :251 sources (the logins read), :413 watch and mail.rs on_unified_push (a change re-reads the
// unified list), :386 refresh_counts; Bulwark lib/unified-mailbox.ts. M2 plan 4.4, 6.5 (push applied as deltas, no reset). Owner unified.
// The engine of the account on screen reports changes (MailSync pokes with an empty EmailsChangedEvent while the unified list is watched);
// the other logins are re-read when their unread count changes (the account manager's two-minute watch), where Rust held a push stream
// per login. Each change re-reads the loaded window and hands the difference to the list as a ListDeltaEvent of the unified query.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Storage;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Unified;

/// <summary>The unified inbox: its settings, its sources and the one list source that serves and follows it.</summary>
public sealed class UnifiedInbox : IStartupTask, IDisposable
{
    private readonly IUnifiedLogins _logins;
    private readonly IAccountOverview? _overview;
    private readonly IPrefsStore _prefs;
    private readonly MailEventHub _hub;
    private int _refreshing;

    public UnifiedInbox(IUnifiedLogins logins, IAccountOverview? overview, IPrefsStore prefs, MailEventHub hub)
    {
        ArgumentNullException.ThrowIfNull(logins);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(hub);
        _logins = logins;
        _overview = overview;
        _prefs = prefs;
        _hub = hub;
        Source = new UnifiedListSource(Sources);
    }

    /// <summary>Raised after the list was told of a change (tests and the automation wait on it).</summary>
    public event EventHandler? Refreshed;

    /// <summary>The list source of the unified inbox (one instance: it remembers the window it served).</summary>
    public UnifiedListSource Source { get; }

    /// <summary>The settings now.</summary>
    public UnifiedSettings Settings => UnifiedSettings.From(_prefs);

    /// <summary>The sources the unified inbox reads now.</summary>
    public IReadOnlyList<UnifiedSource> Sources() =>
        UnifiedSources.Build(_logins.Logins, _logins.Active, Settings);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hub.EventReceived += OnEvent;
        if (_overview is not null)
        {
            _overview.StatusesChanged += OnStatuses;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _hub.EventReceived -= OnEvent;
        if (_overview is not null)
        {
            _overview.StatusesChanged -= OnStatuses;
        }
    }

    /// <summary>Re-reads the unified window and tells the list what changed (nothing when it shows another folder).</summary>
    public async Task RefreshAsync(CancellationToken ct)
    {
        if (Source.Query is not { } query || Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        try
        {
            var delta = await Source.RefreshAsync(ct).ConfigureAwait(false);
            if (delta is { IsOk: true, Value: { } d })
            {
                _hub.Publish(new ListDeltaEvent(d) { Query = query }, _logins.Active);
                Refreshed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private void OnEvent(object? sender, MailSyncEventArgs e)
    {
        if (e.Event is EmailsChangedEvent or NewMailEvent or MailboxesChangedEvent)
        {
            _ = RefreshAsync(CancellationToken.None);
        }
    }

    private void OnStatuses(object? sender, EventArgs e)
    {
        if (Settings.CrossAccount)
        {
            _ = RefreshAsync(CancellationToken.None);
        }
    }
}
