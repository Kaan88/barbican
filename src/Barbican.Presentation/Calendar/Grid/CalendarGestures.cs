// Mirrors: Rust dnd.rs / app.slint (events drag in 15-minute steps and resize with rollback), cal_editor.rs:1431 quick; Bulwark components/calendar/calendar-week-view.tsx, calendar-month-view.tsx, lib/calendar/event-drag-grab.ts, lib/calendar-editability.ts;
// B-CAL-052 (a drop that changes nothing is skipped; a failed drop rolls back and says so); I-314..I-324. The write and its store rollback are ICalendarWriter.MoveAsync (cal-edit); this is the decision.

using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Calendar.Editors;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>The default <see cref="ICalendarGestures"/>.</summary>
public sealed class CalendarGestures : ICalendarGestures
{
    /// <summary>The grid snaps to 15 minutes (B-CAL-052).</summary>
    public const int SnapMinutes = 15;

    /// <summary>A click-created event lasts an hour.</summary>
    public const int DefaultMinutes = 60;

    private readonly ICalendarWriter _writer;
    private readonly ICalendarViewModel _view;
    private readonly ICalendarEditors _editors;
    private readonly IToastService? _toasts;
    private readonly ICalendarSource _source;
    private readonly IAccountManager _accounts;

    public CalendarGestures(ICalendarWriter writer, ICalendarViewModel view, ICalendarEditors editors, ICalendarSource source, IAccountManager accounts, IToastService? toasts = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(editors);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(accounts);
        _writer = writer;
        _view = view;
        _editors = editors;
        _toasts = toasts;
        _source = source;
        _accounts = accounts;
    }

    public DateTime Snap(DateTime time)
    {
        var step = TimeSpan.FromMinutes(SnapMinutes).Ticks;
        var ticks = (long)Math.Round((double)time.Ticks / step, MidpointRounding.AwayFromZero) * step;
        return new DateTime(ticks, DateTimeKind.Unspecified);
    }

    /// <summary>Whether the occurrence can be dragged at all: an event of a calendar the user may write, with no guests the user does not organise.</summary>
    public bool CanDrag(string occurrenceId)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        return Find(occurrenceId) is { Kind: CalendarOccurrenceKind.Event } && _accounts.Active is { } a && _source.CanMove(a.Key, occurrenceId);
    }

    public async Task<Result<Unit>> MoveAsync(string occurrenceId, DateTime newStart, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        if (Refuse(occurrenceId) is { } refusal)
        {
            return refusal;
        }

        var o = Find(occurrenceId)!;
        var start = o.AllDay ? newStart.Date : Snap(newStart);
        if (start == o.Start)
        {
            return Result<Unit>.Ok(default);
        }

        return await WriteAsync(o, start, o.End - o.Start, ct).ConfigureAwait(false);
    }

    public async Task<Result<Unit>> ResizeAsync(string occurrenceId, DateTime newEnd, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);
        if (Refuse(occurrenceId) is { } refusal)
        {
            return refusal;
        }

        var o = Find(occurrenceId)!;
        var end = o.AllDay ? newEnd.Date : Snap(newEnd);
        var minimum = o.AllDay ? o.Start.AddDays(1) : o.Start.AddMinutes(SnapMinutes);
        if (end < minimum)
        {
            end = minimum;
        }

        if (end == o.End)
        {
            return Result<Unit>.Ok(default);
        }

        return await WriteAsync(o, o.Start, end - o.Start, ct).ConfigureAwait(false);
    }

    public async Task CreateAsync(DateTime start, DateTime until, bool allDay, CancellationToken ct)
    {
        var (first, last) = start <= until ? (start, until) : (until, start);
        NewEventRequest request;
        if (allDay)
        {
            // all-day ranges end the day after the last day picked (exclusive, as JSCalendar durations are): one day picked is one day long
            request = new NewEventRequest(first.Date, last.Date.AddDays(1), true);
        }
        else
        {
            var s = Snap(first);
            var e = Snap(last);
            if (e - s < TimeSpan.FromMinutes(SnapMinutes))
            {
                e = s.AddMinutes(DefaultMinutes);
            }

            request = new NewEventRequest(s, e, false);
        }

        try
        {
            await _editors.OpenQuickCreateAsync(request, ct).ConfigureAwait(true);
        }
        catch (PortPendingException)
        {
            // the quick-create popover is cal-ui's; until it lands a drag only chooses the range
        }
    }

    private CalendarOccurrence? Find(string id) => _view.Occurrences.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));

    private Result<Unit>? Refuse(string id)
    {
        if (Find(id) is not { } o)
        {
            return Result<Unit>.Fail(new ValidationError("That event is no longer on screen."));
        }

        if (o.Kind != CalendarOccurrenceKind.Event)
        {
            return Result<Unit>.Fail(new ValidationError("Only events can be moved here."));
        }

        if (_accounts.Active is not { } account)
        {
            return Result<Unit>.Fail(new ValidationError("No account is signed in."));
        }

        if (!_source.CanMove(account.Key, id))
        {
            return Result<Unit>.Fail(new ValidationError("Only the organiser can change this event."));
        }

        return null;
    }

    private async Task<Result<Unit>> WriteAsync(CalendarOccurrence o, DateTime start, TimeSpan duration, CancellationToken ct)
    {
        var account = _accounts.Active!.Key;
        var result = await _writer.MoveAsync(account, o.Id, start, duration, ct).ConfigureAwait(false);
        if (!result.IsOk)
        {
            // the writer already put the store back (B-CAL-052): the grid shows the old time again; the toast says why
            _ = _toasts?.Show(new ToastRequest("Couldn't move \"" + o.Title + "\": " + result.Error!.Message, IsError: true));
        }

        return result;
    }
}
