using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Feeds;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The default source over <see cref="ICalendarStore"/> and the account manager.</summary>
public sealed class CalendarSource : IEditorSource
{
    private readonly ICalendarStore _store;
    private readonly IAccountManager? _accounts;
    private readonly ICalendarSubscriptions? _subscriptions;
    private long _accountVersion;
    private IReadOnlySet<string> _feeds = new HashSet<string>(StringComparer.Ordinal);

    public CalendarSource(ICalendarStore store, IAccountManager? accounts = null, ICalendarSubscriptions? subscriptions = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _accounts = accounts;
        _subscriptions = subscriptions;
        _store.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        if (_accounts is not null)
        {
            _accounts.ActiveChanged += (_, _) => Interlocked.Increment(ref _accountVersion);
        }
    }

    public event EventHandler? Changed;

    public long AccountVersion => Interlocked.Read(ref _accountVersion);

    public string? AccountKey => _accounts?.Active?.Key;

    public IReadOnlyList<string> Mine => _accounts?.Active is { } a ? [a.User.ToLowerInvariant()] : [];

    public IReadOnlyList<JsonNode> Calendars => AccountKey is { } k && _store.Snapshot(k) is { } s ? s.Calendars : [];

    public IReadOnlyList<JsonNode> Events => AccountKey is { } k && _store.Snapshot(k) is { } s ? s.Events : [];

    public IReadOnlySet<string> Subscriptions => _feeds;

    /// <summary>Reads the feed list once (the calendar sidebar and the dialogs call it after a change).</summary>
    public async Task RefreshSubscriptionsAsync(CancellationToken ct)
    {
        if (_subscriptions is null || AccountKey is not { } key)
        {
            return;
        }

        var list = await _subscriptions.ListAsync(key, ct).ConfigureAwait(false);
        _feeds = list.Select(s => s.CalendarId).ToHashSet(StringComparer.Ordinal);
    }
}
