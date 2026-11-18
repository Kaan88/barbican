// Mirrors: M4 plan section 6.3 step 1 and D-M4-16 (the Calendar app is available in the rail when the login advertises urn:ietf:params:jmap:calendars); Bulwark components/layout/navigation-rail.tsx (apps by capability).

using Barbican.Core.Accounts;
using Barbican.Presentation.Common;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Keeps Calendar in <see cref="ShellViewModel.AvailableApps"/> exactly while the active login advertises the calendars capability (and a sample run always has it).</summary>
public sealed class CalendarAppAvailability : IDisposable
{
    public const string CalendarsCapability = "urn:ietf:params:jmap:calendars";

    private readonly ShellViewModel _shell;
    private readonly IAccountManager _accounts;
    private readonly IAccountOverview? _overview;
    private readonly ICalendarSource _source;
    private readonly IDispatcher _dispatcher;

    public CalendarAppAvailability(ShellViewModel shell, IAccountManager accounts, ICalendarSource source, IDispatcher dispatcher, IAccountOverview? overview = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _shell = shell;
        _accounts = accounts;
        _source = source;
        _dispatcher = dispatcher;
        _overview = overview;
        _accounts.ActiveChanged += OnChanged;
        if (_overview is not null)
        {
            _overview.StatusesChanged += OnStatuses;
        }
    }

    /// <summary>Whether the active login offers Calendars (a session that is not connected yet does not).</summary>
    public bool LoginHasCalendars =>
        _source.DefaultAccountKey is not null
        || (_accounts.Active is { } active && _accounts.SessionFor(active.Key) is { } session && session.Client.Session.Has(CalendarsCapability));

    /// <summary>Sets Calendar in or out of the available apps; when it goes out while it is open, Mail comes back.</summary>
    public void Apply()
    {
        var has = LoginHasCalendars;
        var current = _shell.AvailableApps;
        if (current.Contains(AppId.Calendar) == has)
        {
            return;
        }

        var next = has ? current.Append(AppId.Calendar).Distinct().OrderBy(a => (int)a).ToList() : current.Where(a => a != AppId.Calendar).ToList();
        _shell.SetAvailableApps(next);
        if (!has && _shell.ActiveApp == AppId.Calendar)
        {
            _ = _shell.TryActivate(AppId.Mail);
        }
    }

    public void Dispose()
    {
        _accounts.ActiveChanged -= OnChanged;
        if (_overview is not null)
        {
            _overview.StatusesChanged -= OnStatuses;
        }
    }

    private void OnChanged(object? sender, ActiveAccountChangedEventArgs e) => Post();

    private void OnStatuses(object? sender, EventArgs e) => Post();

    private void Post()
    {
        if (_dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            _dispatcher.Post(Apply);
        }
    }
}
