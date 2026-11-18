// Mirrors: Rust reminders.rs EdRow (an alert row), cal_editor.rs guests and availability rows; Bulwark components/calendar/alert-list-editor.tsx, participant-input.tsx, participant-availability.tsx.
// Small view-model parts of the event editor.

using Barbican.Core.Calendar.Reminders;
using CommunityToolkit.Mvvm.ComponentModel;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>One alert row: an amount, a unit (0 at time of event, 1 minutes, 2 hours, 3 days, 4 weeks) and how it fires (0 notification, 1 email). A row read from an event keeps its alert id until it is changed.</summary>
public sealed class AlertRowViewModel : ObservableObject
{
    /// <summary>The unit names in index order.</summary>
    public static IReadOnlyList<string> Units { get; } = ["At time of event", "minutes", "hours", "days", "weeks"];

    /// <summary>The action names in index order.</summary>
    public static IReadOnlyList<string> Actions { get; } = ["Notification", "Email"];

    private long _value;
    private int _unit;
    private int _action;

    public AlertRowViewModel(ReminderRow row, AlertSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        _value = row.Value;
        _unit = row.Unit;
        _action = row.Action;
        Source = source;
    }

    public AlertSource? Source { get; }

    public long Value
    {
        get => _value;
        set => SetProperty(ref _value, Math.Max(0, value));
    }

    public int Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, Math.Clamp(value, 0, 4));
    }

    public int Action
    {
        get => _action;
        set => SetProperty(ref _action, Math.Clamp(value, 0, 1));
    }

    /// <summary>The "at time of event" unit has no amount.</summary>
    public bool HasAmount => _unit != 0;

    public ReminderRow Row => new(_value, _unit, _action);

    public AlertEditRow ToEditRow() => new(Row, Source);
}
