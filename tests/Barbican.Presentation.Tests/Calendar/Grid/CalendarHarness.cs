// The fakes of the cal-views tests: an in-memory calendar source, an account manager with one active login, a recording writer, toasts and editors, a controllable clock. Owner cal-views.

#pragma warning disable MA0048 // the fakes of one harness live in one file
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Common;
using Barbican.Presentation.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Presentation.Tests.Calendar.Grid;

internal sealed class FakeCalendarSource : ICalendarSource
{
    public event EventHandler<CalendarSourceChangedEventArgs>? Changed;

    public List<CalendarOccurrence> Items { get; } = [];

    public List<CalendarListRow> Rows { get; } = [new("work", "acc", "Work", "#3b82f6", true, true, false, false, true), new("home", "acc", "Home", "#22c55e", true, false, false, false, true)];

    public HashSet<string> Immovable { get; } = new(StringComparer.Ordinal);

    public Result<Unit> LoadResult { get; set; } = Result<Unit>.Ok(default);

    public int Loads { get; private set; }

    public Task<Result<Unit>> LoadAsync(string accountKey, CancellationToken ct)
    {
        Loads++;
        return Task.FromResult(LoadResult);
    }

    public IReadOnlyList<CalendarListRow> Calendars(string accountKey) => [.. Rows];

    public IReadOnlyList<CalendarOccurrence> OccurrencesIn(string accountKey, DateOnly from, DateOnly until) =>
        [.. Items.Where(o => DateOnly.FromDateTime(o.Start) < until && (o.End > o.Start ? DateOnly.FromDateTime(o.End.AddTicks(-1)) : DateOnly.FromDateTime(o.Start)) >= from)];

    public IReadOnlyList<CalendarOccurrence> Search(string accountKey, string query, DateOnly around, int max) =>
        [.. Items.Where(o => o.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(max)];

    public bool CanMove(string accountKey, string occurrenceId) => !Immovable.Contains(occurrenceId);

    public JsonNode? EventObject(string accountKey, string eventId) => null;

    public void Raise(bool calendars = false) => Changed?.Invoke(this, new CalendarSourceChangedEventArgs("acc", calendars));
}

internal sealed class OneAccount : IAccountManager
{
    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged;

    public AccountInfo? Active { get; private set; } = new("acc", "me@example.org", "https://jmap.example.org");

    public IReadOnlyList<AccountInfo> Accounts => Active is null ? [] : [Active];

    public void Switch(AccountInfo? next)
    {
        var previous = Active;
        Active = next;
        ActiveChanged?.Invoke(this, new ActiveAccountChangedEventArgs(previous, next));
    }

    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

    public IAccountSession? SessionFor(string accountKey) => null;
}

internal sealed class RecordingWriter : ICalendarWriter
{
    public List<(string Account, string Id, DateTime Start, TimeSpan Duration)> Moves { get; } = [];

    public Result<Unit> MoveResult { get; set; } = Result<Unit>.Ok(default);

    public Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct)
    {
        Moves.Add((accountKey, occurrenceId, newStart, duration));
        return Task.FromResult(MoveResult);
    }

    public Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<string>> CreateAsync(string accountKey, string calendarId, JsonObject data, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> UpdateAsync(string accountKey, string id, JsonObject patch, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class RecordingToastService : IToastService
{
    public List<ToastRequest> Shown { get; } = [];

    public ToastHandle Show(ToastRequest toast)
    {
        Shown.Add(toast);
        return default!;
    }

    public void Dismiss(ToastHandle handle)
    {
    }
}

internal sealed class RecordingEditors : ICalendarEditors
{
    public List<string> Opened { get; } = [];

    public List<NewEventRequest> QuickCreates { get; } = [];

    public Task OpenQuickCreateAsync(NewEventRequest request, CancellationToken ct)
    {
        QuickCreates.Add(request);
        return Task.CompletedTask;
    }

    public Task OpenNewEventAsync(NewEventRequest request, CancellationToken ct) => Task.CompletedTask;

    public Task OpenAsync(string occurrenceId, CancellationToken ct)
    {
        Opened.Add(occurrenceId);
        return Task.CompletedTask;
    }

    public Task OpenNewTaskAsync(string? calendarId, DateOnly? due, CancellationToken ct) => Task.CompletedTask;

    public Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct) => Task.FromResult<(EditScope, bool)?>(null);
}

internal sealed class CalendarHarness : IDisposable
{
    public CalendarHarness(string prefs = "{}")
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        Time.SetLocalTimeZone(TimeZoneInfo.Utc);
        Prefs = new TestPrefs(prefs);
        Bus = new HookBus();
        Source = new FakeCalendarSource();
        Accounts = new OneAccount();
        Writer = new RecordingWriter();
        Toasts = new RecordingToastService();
        Editors = new RecordingEditors();
        View = new CalendarViewModel(Source, Accounts, Prefs, Bus, Time, new InlineDispatcher(), Editors);
        Gestures = new CalendarGestures(Writer, View, Editors, Source, Accounts, Toasts);
    }

    public FakeTimeProvider Time { get; }

    public TestPrefs Prefs { get; }

    public HookBus Bus { get; }

    public FakeCalendarSource Source { get; }

    public OneAccount Accounts { get; }

    public RecordingWriter Writer { get; }

    public RecordingToastService Toasts { get; }

    public RecordingEditors Editors { get; }

    public CalendarViewModel View { get; }

    public CalendarGestures Gestures { get; }

    public static DateTime Dt(string s) => DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public CalendarOccurrence Add(string id, string title, string start, string end, string calendar = "work", bool allDay = false, CalendarOccurrenceKind kind = CalendarOccurrenceKind.Event, string location = "")
    {
        var o = new CalendarOccurrence(id, id.Split('|')[0], null, title, Dt(start), Dt(end), allDay, calendar, location, kind, false);
        Source.Items.Add(o);
        return o;
    }

    /// <summary>Loads and shows what was added.</summary>
    public async Task ReloadAsync() => await View.LoadAsync(CancellationToken.None);

    /// <summary>Waits for the fire-and-forget hook chain (the bus notifies on the thread pool).</summary>
    public static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "the condition did not become true");
    }

    public void Dispose() => View.Dispose();
}
