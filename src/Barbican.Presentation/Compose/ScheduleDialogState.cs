// Mirrors: Rust schedule.rs (presets :26, parse_local :40, validate :64); Bulwark components/email/schedule-send-dialog.tsx (presets, a typed date and time, the same validation messages); I-241.
// The schedule dialog of the composer: the one-click presets, a typed date and time, and the error of the last try. Rules come from send-pipeline's ScheduleRules.

using Barbican.Core.Compose.Schedule;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Compose;

/// <summary>The state of one schedule dialog; <see cref="Result"/> completes with the chosen time, or null when cancelled.</summary>
public sealed class ScheduleDialogState : ObservableObject
{
    private readonly TaskCompletionSource<DateTimeOffset?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;
    private readonly long _maxSeconds;
    private string _date = string.Empty;
    private string _clock = string.Empty;
    private string? _error;

    public ScheduleDialogState(ScheduleDialogMode mode, TimeProvider time, TimeZoneInfo zone, long maxSeconds)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(zone);
        Mode = mode;
        _time = time;
        _zone = zone;
        _maxSeconds = maxSeconds;
        Presets = ScheduleRules.Presets(time.GetUtcNow(), zone);
    }

    public ScheduleDialogMode Mode { get; }

    public string Title => Mode == ScheduleDialogMode.Reschedule ? "Reschedule" : "Schedule send";

    public IReadOnlyList<SchedulePreset> Presets { get; }

    /// <summary>The typed date (2026-11-17, 17.11.2026 or 17/11/2026).</summary>
    public string Date
    {
        get => _date;
        set => SetProperty(ref _date, value ?? string.Empty);
    }

    /// <summary>The typed time (08:00, 8:30 pm).</summary>
    public string Time
    {
        get => _clock;
        set => SetProperty(ref _clock, value ?? string.Empty);
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

#pragma warning disable VSTHRD003 // completed by the dialog's own buttons
    public Task<DateTimeOffset?> Result => _result.Task;
#pragma warning restore VSTHRD003

    /// <summary>A preset button.</summary>
    public void Choose(int index)
    {
        if (index >= 0 && index < Presets.Count)
        {
            Take(Presets[index].At, filled: true);
        }
    }

    /// <summary>The typed date and time.</summary>
    public void ChooseTyped()
    {
        var filled = _date.Trim().Length > 0 && _clock.Trim().Length > 0;
        Take(filled ? ScheduleRules.ParseLocal(_date, _clock, _zone) : null, filled);
    }

    public void Cancel() => _result.TrySetResult(null);

    private void Take(DateTimeOffset? when, bool filled)
    {
        var checkedTime = ScheduleRules.Validate(when, _time.GetUtcNow(), _maxSeconds, filled);
        if (checkedTime.IsOk)
        {
            Error = null;
            _result.TrySetResult(checkedTime.Value);
            return;
        }

        Error = checkedTime.Error!.Message;
    }
}
