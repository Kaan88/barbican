// Mirrors: Rust schedule.rs:104-155 (open_dialog: presets, tomorrow 8:00 as the typed default or the message's own time, "Scheduled send is not supported for this account." up front; choose: preset or typed, validate, close), :152 cancel;
// Bulwark components/email/schedule-send-dialog.tsx, lib/schedule-presets.ts. I-241 (schedule dialog, Reschedule). Owner send-pipeline; the dialog view is composer-ui's.

using System.ComponentModel;
using System.Globalization;
using Barbican.Core.Compose.Schedule;

namespace Barbican.Presentation.Compose.Pipeline;

/// <summary>The data and rules of the "Schedule send" / "Reschedule" dialog: presets, a typed date and time, the checks, and the chosen instant.</summary>
public sealed class ScheduleDialogViewModel : INotifyPropertyChanged
{
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly long _maxSeconds;
    private readonly TaskCompletionSource<DateTimeOffset?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _date;
    private string _time_;
    private string _error;

    /// <param name="mode">Compose or Reschedule.</param>
    /// <param name="time">The clock (and the zone: <see cref="TimeProvider.LocalTimeZone"/>).</param>
    /// <param name="maxDelayedSend">The account's longest hold in seconds (<c>IScheduledSends.MaxDelayedSend</c>); 0: scheduling is not supported.</param>
    /// <param name="twelveHour">The time format setting (12h).</param>
    /// <param name="current">The message's own release time when rescheduling: the typed default while it is still ahead.</param>
    /// <param name="zone">The profile display zone, or the clock's local zone when omitted.</param>
    public ScheduleDialogViewModel(ScheduleMode mode, TimeProvider time, long maxDelayedSend, bool twelveHour, DateTimeOffset? current = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _zone = zone ?? time.LocalTimeZone;
        _maxSeconds = maxDelayedSend;
        Mode = mode;
        TwelveHour = twelveHour;
        var now = time.GetUtcNow();
        zone = _zone;
        Presets = [.. ScheduleRules.Presets(now, zone).Select(p => new SchedulePresetRow(p.Label, When(p.At, zone, twelveHour), p.At))];
        var start = current is { } c && c > now ? c : Presets.FirstOrDefault(p => string.Equals(p.Label, "Tomorrow morning", StringComparison.Ordinal))?.At ?? now.AddDays(1);
        var local = TimeZoneInfo.ConvertTime(start, zone);
        _date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _time_ = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        _error = maxDelayedSend == 0 ? "Scheduled send is not supported for this account." : "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ScheduleMode Mode { get; }

    public bool TwelveHour { get; }

    /// <summary>"Schedule send" or "Reschedule".</summary>
    public string Title => Mode == ScheduleMode.Reschedule ? "Reschedule" : "Schedule send";

    /// <summary>The confirm button's text (the same words).</summary>
    public string ConfirmLabel => Title;

    public IReadOnlyList<SchedulePresetRow> Presets { get; }

    /// <summary>The typed date (2026-11-17, 17.11.2026, 17/11/2026).</summary>
    public string Date
    {
        get => _date;
        set => Set(ref _date, value, nameof(Date));
    }

    /// <summary>The typed time (08:00, 8:30 pm, 20.15).</summary>
    public string Time
    {
        get => _time_;
        set => Set(ref _time_, value, nameof(Time));
    }

    /// <summary>The dialog's message under the fields; empty when all is well.</summary>
    public string Error
    {
        get => _error;
        private set => Set(ref _error, value, nameof(Error));
    }

    /// <summary>Completes with the chosen instant, or null when the dialog was cancelled.</summary>
#pragma warning disable VSTHRD003 // the task belongs to this view model: the dialog's own completion source, not work started elsewhere
    public Task<DateTimeOffset?> Completion => _done.Task;
#pragma warning restore VSTHRD003

    /// <summary>A preset was clicked: validates it (it can be too far ahead for the server, or the minute may have passed) and closes with it.</summary>
    public DateTimeOffset? Choose(int index)
    {
        if (index < 0 || index >= Presets.Count)
        {
            return null;
        }

        return Finish(ScheduleRules.Validate(Presets[index].At, _time.GetUtcNow(), _maxSeconds, true));
    }

    /// <summary>The typed date and time were confirmed.</summary>
    public DateTimeOffset? ChooseTyped()
    {
        var filled = Date.Trim().Length > 0 && Time.Trim().Length > 0;
        var when = ScheduleRules.ParseLocal(Date, Time, _zone);
        return Finish(ScheduleRules.Validate(when, _time.GetUtcNow(), _maxSeconds, filled));
    }

    /// <summary>The dialog was dismissed.</summary>
    public void Cancel() => _done.TrySetResult(null);

    private DateTimeOffset? Finish(Barbican.Core.Common.Result<DateTimeOffset> checkedTime)
    {
        if (!checkedTime.IsOk)
        {
            Error = checkedTime.Error!.Message;
            return null;
        }

        Error = "";
        _done.TrySetResult(checkedTime.Value);
        return checkedTime.Value;
    }

    private static string When(DateTimeOffset at, TimeZoneInfo zone, bool twelveHour)
    {
        var local = TimeZoneInfo.ConvertTime(at, zone);
        return local.ToString("ddd d MMM", CultureInfo.InvariantCulture) + ", " + local.ToString(twelveHour ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
    }

    private void Set(ref string field, string value, string name)
    {
        if (!string.Equals(field, value, StringComparison.Ordinal))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
