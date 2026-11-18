// Mirrors: Rust cal_editor.rs:898-:990 (rr_begin, rr_rule, rr_toggle_day: at least one weekday stays on), Bulwark components/calendar/recurrence-editor.tsx (every N, weekly weekday chips, monthly "on day N" or "on the nth weekday",
// yearly month, ends never / on a date / after N occurrences), B-CAL-017; I-336. The custom recurrence of the event editor: a rule and its live summary, no UI types.

using System.Globalization;
using Barbican.Core.Calendar.Recurrence;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The custom recurrence editor: frequency, interval, weekdays, month day or nth weekday, month, end.</summary>
public sealed class RecurrenceEditorViewModel : ObservableObject
{
    /// <summary>The "nth" choices of "on the nth weekday": first to fourth and last.</summary>
    public static IReadOnlyList<(long Value, string Label)> NthChoices { get; } = [(1, "first"), (2, "second"), (3, "third"), (4, "fourth"), (-1, "last")];

    /// <summary>The frequencies, in index order: daily 0, weekly 1, monthly 2, yearly 3.</summary>
    public static IReadOnlyList<string> Frequencies { get; } = ["daily", "weekly", "monthly", "yearly"];

    private readonly ICalendarRules _rules;
    private readonly HashSet<DayOfWeek> _days = [];
    private int _freq = 1;
    private string _interval = "1";
    private int _mode;
    private string _monthDay = "1";
    private int _nth;
    private DayOfWeek _nthDay = DayOfWeek.Monday;
    private int _month;
    private int _ends;
    private string _until = string.Empty;
    private string _count = "12";
    private DayOfWeek _startDay = DayOfWeek.Monday;

    public RecurrenceEditorViewModel(ICalendarRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <summary>0 daily, 1 weekly, 2 monthly, 3 yearly.</summary>
    public int Freq
    {
        get => _freq;
        set => Set(ref _freq, Math.Clamp(value, 0, 3));
    }

    public string Interval
    {
        get => _interval;
        set => Set(ref _interval, value ?? string.Empty);
    }

    /// <summary>0 "on day N", 1 "on the nth weekday" (monthly and yearly).</summary>
    public int Mode
    {
        get => _mode;
        set => Set(ref _mode, Math.Clamp(value, 0, 1));
    }

    public string MonthDay
    {
        get => _monthDay;
        set => Set(ref _monthDay, value ?? string.Empty);
    }

    /// <summary>An index into <see cref="NthChoices"/>.</summary>
    public int Nth
    {
        get => _nth;
        set => Set(ref _nth, Math.Clamp(value, 0, 4));
    }

    public DayOfWeek NthDay
    {
        get => _nthDay;
        set => Set(ref _nthDay, value);
    }

    /// <summary>Yearly: 0 January to 11 December.</summary>
    public int Month
    {
        get => _month;
        set => Set(ref _month, Math.Clamp(value, 0, 11));
    }

    /// <summary>0 never, 1 on a date, 2 after N occurrences.</summary>
    public int Ends
    {
        get => _ends;
        set => Set(ref _ends, Math.Clamp(value, 0, 2));
    }

    /// <summary>The last day, "yyyy-MM-dd".</summary>
    public string Until
    {
        get => _until;
        set => Set(ref _until, value ?? string.Empty);
    }

    public string Count
    {
        get => _count;
        set => Set(ref _count, value ?? string.Empty);
    }

    /// <summary>The weekdays of a weekly rule.</summary>
    public IReadOnlyCollection<DayOfWeek> Weekdays => _days;

    /// <summary>The rule as the editor stands.</summary>
    public RecurrenceRule Rule => BuildRule();

    /// <summary>The live summary line (<c>RecurrenceRule.Summary</c>).</summary>
    public string Summary => _rules.Summary(BuildRule());

    /// <summary>Toggles a weekday chip; at least one day stays on.</summary>
    public void ToggleDay(DayOfWeek day)
    {
        if (!_days.Add(day) && _days.Count > 1)
        {
            _days.Remove(day);
        }

        Raise(nameof(Weekdays));
    }

    public bool IsDayOn(DayOfWeek day) => _days.Contains(day);

    /// <summary>Starts from a rule (or a plain weekly one), with the event's start for the defaults (Rust rr_begin).</summary>
    public void Load(RecurrenceRule? rule, DateTime start)
    {
        var r = rule ?? RecurrenceRule.Simple("weekly");
        _startDay = start.DayOfWeek;
        var fi = Frequencies.ToList().FindIndex(f => string.Equals(f, r.Freq, StringComparison.Ordinal));
        _freq = fi < 0 ? 1 : fi;
        _interval = r.Interval.ToString(CultureInfo.InvariantCulture);
        _days.Clear();
        if (string.Equals(r.Freq, "weekly", StringComparison.Ordinal) && r.ByDay.Count > 0)
        {
            foreach (var d in r.ByDay)
            {
                _days.Add(d.Day);
            }
        }
        else
        {
            _days.Add(start.DayOfWeek);
        }

        var nth = r.ByDay.FirstOrDefault(d => d.Nth is not null);
        _mode = nth is not null ? 1 : 0;
        _monthDay = (r.ByMonthDay.Count > 0 ? r.ByMonthDay[0] : start.Day).ToString(CultureInfo.InvariantCulture);
        _nth = nth?.Nth is { } n ? Math.Max(0, IndexOfNth(n)) : Math.Min((start.Day - 1) / 7, 3);
        _nthDay = nth?.Day ?? start.DayOfWeek;
        _month = r.ByMonth.Count > 0 ? r.ByMonth[0] - 1 : start.Month - 1;
        _ends = r.Count is not null ? 2 : r.Until is not null ? 1 : 0;
        _until = (r.Until ?? start.AddDays(365)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _count = (r.Count ?? 12).ToString(CultureInfo.InvariantCulture);
        RaiseAll();
    }

    private static int IndexOfNth(long n)
    {
        for (var i = 0; i < NthChoices.Count; i++)
        {
            if (NthChoices[i].Value == n)
            {
                return i;
            }
        }

        return 0;
    }

    private RecurrenceRule BuildRule()
    {
        var freq = Frequencies[_freq];
        var interval = long.TryParse(_interval.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) ? Math.Clamp(iv, 1, 999) : 1;
        var rule = RecurrenceRule.Simple(freq) with { Interval = interval };
        switch (freq)
        {
            case "weekly":
                var days = _days.Count == 0 ? [_startDay] : _days.OrderBy(d => ((int)d + 6) % 7).ToList();
                rule = rule with { ByDay = [.. days.Select(d => new ByDayEntry(d))] };
                break;
            case "monthly" or "yearly":
                rule = _mode == 1
                    ? rule with { ByDay = [new ByDayEntry(_nthDay, NthChoices[_nth].Value)] }
                    : rule with { ByMonthDay = [long.TryParse(_monthDay.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var md) ? Math.Clamp(md, 1, 31) : 1] };
                if (string.Equals(freq, "yearly", StringComparison.Ordinal))
                {
                    rule = rule with { ByMonth = [_month + 1] };
                }

                break;
            default:
                break;
        }

        return _ends switch
        {
            1 when DateOnly.TryParseExact(_until.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var u) => rule with { Until = u.ToDateTime(new TimeOnly(23, 59, 59)) },
            2 => rule with { Count = long.TryParse(_count.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? Math.Clamp(c, 1, 9999) : 1 },
            _ => rule,
        };
    }

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        RaiseAll();
    }

    private void Raise(string name)
    {
        OnPropertyChanged(name);
        OnPropertyChanged(nameof(Rule));
        OnPropertyChanged(nameof(Summary));
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty);
    }
}
