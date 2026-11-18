// Mirrors: Rust recurrence.rs:13 weekday_of, :18 parse_local, :24 fmt_local, :27 duration_secs, :52 duration_string, :228 same_rules, :293 rule_starts, :377 event_duration, :386 occurrences, :427 occurrences_before;
// RFC 8984 4.3 (recurrence on the event's own wall clock, overrides, excludedRecurrenceRules), RFC 5545 3.3.6 (durations); Bulwark lib/recurrence-rule.ts, components/calendar/recurrence-editor.tsx.
// Wall-clock values are DateTime with Kind Unspecified (the engine uses NodaTime only for zones). Contract from the M4 plan section 4.1; owner cal-data.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Recurrence;

/// <summary>JSCalendar recurrence expansion and the small date helpers the calendar shares. Pure; no clock, no zone database except through <see cref="TimeZones"/>.</summary>
public static class RecurrenceEngine
{
    /// <summary>The expansion stops after this many loop turns (Rust: a rule that never matches must end).</summary>
    private const int LoopGuard = 20_000;

    private const int PeriodGuard = 50_000;

    /// <summary>A LocalDateTime "YYYY-MM-DDTHH:MM:SS" (a trailing Z or fraction is dropped; a bare date is midnight); null when it does not parse.</summary>
    public static DateTime? ParseLocal(string text)
    {
        if (text is null)
        {
            return null;
        }

        var s = text.Trim().TrimEnd('Z');
        if (DateTime.TryParseExact(s.Length > 19 ? s[..19] : s, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            return DateTime.SpecifyKind(t, DateTimeKind.Unspecified);
        }

        return DateTime.TryParseExact(s.Length > 10 ? s[..10] : s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified) : null;
    }

    public static string FormatLocal(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>ISO 8601 duration (PnW, PnDTnHnMnS, optional sign) in seconds; null for "PT", "bogus".</summary>
    public static long? DurationSeconds(string text)
    {
        if (text is null)
        {
            return null;
        }

        var s = text.Trim();
        var negative = false;
        if (s.StartsWith('-'))
        {
            negative = true;
            s = s[1..];
        }
        else if (s.StartsWith('+'))
        {
            s = s[1..];
        }

        if (!s.StartsWith('P'))
        {
            return null;
        }

        long total = 0;
        var digits = new StringBuilder();
        var inTime = false;
        var any = false;
        foreach (var ch in s.AsSpan(1))
        {
            if (ch == 'T')
            {
                inTime = true;
            }
            else if (ch is >= '0' and <= '9')
            {
                _ = digits.Append(ch);
            }
            else
            {
                if (!long.TryParse(digits.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    return null;
                }

                _ = digits.Clear();
                any = true;
                if (UnitSeconds(ch, inTime) is not { } unit)
                {
                    return null;
                }

                total += n * unit;
            }
        }

        return digits.Length > 0 || !any ? null : negative ? -total : total;
    }

    private static long? UnitSeconds(char unit, bool inTime) => (unit, inTime) switch
    {
        ('W', false) => 7 * 86400,
        ('D', false) => 86400,
        ('H', true) => 3600,
        ('M', true) => 60,
        ('S', true) => 1,
        _ => null,
    };

    /// <summary>"PT1H30M", "P2D", "P1DT2H", "PT0S".</summary>
    public static string DurationString(long seconds)
    {
        var secs = Math.Max(0, seconds);
        var (d, h, m, s) = (secs / 86400, secs % 86400 / 3600, secs % 3600 / 60, secs % 60);
        var o = new StringBuilder("P");
        if (d > 0)
        {
            _ = o.Append(CultureInfo.InvariantCulture, $"{d}D");
        }

        if (h > 0 || m > 0 || s > 0)
        {
            _ = o.Append('T');
            if (h > 0)
            {
                _ = o.Append(CultureInfo.InvariantCulture, $"{h}H");
            }

            if (m > 0)
            {
                _ = o.Append(CultureInfo.InvariantCulture, $"{m}M");
            }

            if (s > 0)
            {
                _ = o.Append(CultureInfo.InvariantCulture, $"{s}S");
            }
        }

        return o.Length == 1 ? "PT0S" : o.ToString();
    }

    /// <summary>The event's duration; an all-day event lasts at least one day, a timed one defaults to one hour.</summary>
    public static TimeSpan EventDuration(JsonNode ev)
    {
        var allDay = ev.IsTrue("showWithoutTime");
        var secs = (ev.Str("duration") is { } d ? DurationSeconds(d) : null) ?? (allDay ? 86400 : 3600);
        secs = secs <= 0 && allDay ? 86400 : Math.Max(0, secs);
        return TimeSpan.FromSeconds(secs);
    }

    /// <summary>The start times of <paramref name="rule"/> from <paramref name="start"/> (the start counts as the first), before <paramref name="to"/>, at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<DateTime> RuleStarts(DateTime start, RecurrenceRule rule, DateTime to, int max)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var t = start.TimeOfDay;
        var output = new List<DateTime> { start };
        long n = 1;
        if (rule.Count == 1)
        {
            return output;
        }

        long period = 0;
        var guard = 0;
        try
        {
            while (true)
            {
                guard++;
                if (guard > LoopGuard || output.Count >= max)
                {
                    break;
                }

                var cands = Candidates(start, t, rule, period);
                if (cands is null)
                {
                    break;
                }

                // a period that starts after `to` ends the expansion
                var periodPast = cands.Count > 0 && cands[0] >= to;
                if (!TakeCandidates(cands, start, rule, to, output, ref n))
                {
                    return output;
                }

                // runaway: no candidate for very long (a rule that never matches)
                if (periodPast || (rule.Count is { } k2 && n >= k2) || period > PeriodGuard)
                {
                    break;
                }

                period++;
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException)
        {
            // past the last date a DateTime holds: the expansion ends there
        }

        return output;
    }

    /// <summary>Adds the candidates of one period after the start; false when the rule ends there (until, to or count reached).</summary>
    private static bool TakeCandidates(List<DateTime> cands, DateTime start, RecurrenceRule rule, DateTime to, List<DateTime> output, ref long n)
    {
        foreach (var c in cands)
        {
            if (c <= start)
            {
                continue;
            }

            if ((rule.Until is { } u && c > u) || c >= to || (rule.Count is { } k && n >= k))
            {
                return false;
            }

            n++;
            output.Add(c);
        }

        return true;
    }

    /// <summary>The occurrences of <paramref name="ev"/> whose wall-clock span overlaps [from, to), with excluded overrides left out, moving overrides applied and override dates no rule gives added.</summary>
    public static IReadOnlyList<Occurrence> Occurrences(JsonNode ev, DateTime from, DateTime to)
    {
        if (ev.Str("start") is not { } startText || ParseLocal(startText) is not { } start)
        {
            return [];
        }

        var dur = EventDuration(ev);
        var rules = ev.Arr("recurrenceRules") is { } ra ? ra.Where(r => r is not null).Select(r => RecurrenceRule.Parse(r!)).ToList() : [];
        var overrides = ev.Obj("recurrenceOverrides");
        if (rules.Count == 0 && (overrides is null || overrides.Count == 0))
        {
            return start < to && start + dur > from ? [new Occurrence(start, start + dur, null, null)] : [];
        }

        var starts = new List<DateTime>();
        foreach (var r in rules)
        {
            starts.AddRange(RuleStarts(start, r, to, 100_000));
        }

        if (rules.Count == 0)
        {
            starts.Add(start);
        }

        // excluded rules (RFC 8984 excludedRecurrenceRules)
        if (ev.Arr("excludedRecurrenceRules") is { } ex)
        {
            var excluded = new HashSet<DateTime>(ex.Where(r => r is not null).SelectMany(r => RuleStarts(start, RecurrenceRule.Parse(r!), to, 100_000)));
            _ = starts.RemoveAll(excluded.Contains);
        }

        var keyed = KeyedStarts(starts, overrides, to);

        var result = new List<Occurrence>();
        foreach (var (rid, patch) in keyed)
        {
            if (patch.IsTrue("excluded"))
            {
                continue;
            }

            var s = (patch.Str("start") is { } ps ? ParseLocal(ps) : null) ?? rid;
            var d = patch.Str("duration") is { } pd && DurationSeconds(pd) is { } pds ? TimeSpan.FromSeconds(pds) : dur;
            if (s < to && s + d > from)
            {
                result.Add(new Occurrence(s, s + d, FormatLocal(rid), patch?.DeepClone()));
            }
        }

        return [.. result.OrderBy(o => o.Start)];
    }

    /// <summary>The start times with the override that belongs to each, and the override dates no rule gives (RDATE-like keys).</summary>
    private static List<(DateTime Id, JsonNode? Patch)> KeyedStarts(List<DateTime> starts, JsonObject? overrides, DateTime to)
    {
        var ovr = new List<(DateTime Key, JsonNode Value)>();
        foreach (var (k, v) in overrides ?? [])
        {
            if (v is not null && ParseLocal(k) is { } key)
            {
                ovr.Add((key, v));
            }
        }

        var keyed = new List<(DateTime Id, JsonNode? Patch)>();
        foreach (var s in starts.Distinct().Order())
        {
            keyed.Add((s, ovr.Find(o => o.Key == s) is { Value: { } p } ? p : null));
        }

        // RDATE-like override keys no rule gives
        foreach (var (k, v) in ovr)
        {
            if (!keyed.Exists(x => x.Id == k) && k < to)
            {
                keyed.Add((k, v));
            }
        }

        return keyed;
    }

    /// <summary>How many occurrences of the rule (count ignored) come before <paramref name="split"/>: the COUNT left to a series split off there.</summary>
    public static long OccurrencesBefore(DateTime seriesStart, RecurrenceRule rule, DateTime split)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return RuleStarts(seriesStart, rule with { Count = null }, split, 1_000_000).Count;
    }

    /// <summary>Two <c>recurrenceRules</c> arrays are the same when their parsed rules are (order of byDay, @type and default fields ignored).</summary>
    public static bool SameRules(JsonNode a, JsonNode b)
    {
        static List<string> Norm(JsonNode v)
        {
            var list = v is JsonArray arr ? arr.Where(r => r is not null).Select(r => Key(RecurrenceRule.Parse(r!))).ToList() : [];
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        return Norm(a).SequenceEqual(Norm(b), StringComparer.Ordinal);
    }

    private static string Key(RecurrenceRule r)
    {
        var byDay = string.Join(',', r.ByDay.OrderBy(d => RecurrenceRule.MondayIndex(d.Day)).ThenBy(d => d.Nth).Select(d => string.Create(CultureInfo.InvariantCulture, $"{d.Day}:{d.Nth}")));
        static string L<T>(IEnumerable<T> v) => string.Join(',', v.Order().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)));
        return string.Create(CultureInfo.InvariantCulture, $"{r.Freq}|{r.Interval}|{byDay}|{L(r.ByMonthDay)}|{L(r.ByMonth)}|{L(r.BySetPos)}|{r.Count}|{r.Until:O}|{r.FirstDayOfWeek}");
    }

    /// <summary>Days of month <paramref name="m"/> the rule's byDay / byMonthDay give (or the start's day when neither is set).</summary>
    private static List<DateTime> MonthDays(RecurrenceRule r, int y, int m, int startDay)
    {
        long dim = DateTime.DaysInMonth(y, m);
        var output = new List<DateTime>();
        if (r.ByMonthDay.Count > 0)
        {
            foreach (var md in r.ByMonthDay)
            {
                var d = md < 0 ? dim + 1 + md : md;
                if (d >= 1 && d <= dim)
                {
                    output.Add(new DateTime(y, m, (int)d, 0, 0, 0, DateTimeKind.Unspecified));
                }
            }

            if (r.ByDay.Count > 0)
            {
                _ = output.RemoveAll(d => !r.ByDay.Any(w => w.Day == d.DayOfWeek));
            }
        }
        else if (r.ByDay.Count > 0)
        {
            foreach (var (w, nth) in r.ByDay.Select(x => (x.Day, x.Nth)))
            {
                var all = Enumerable.Range(1, (int)dim).Select(d => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Unspecified)).Where(d => d.DayOfWeek == w).ToList();
                if (nth is > 0)
                {
                    if (nth.Value - 1 < all.Count)
                    {
                        output.Add(all[(int)nth.Value - 1]);
                    }
                }
                else if (nth is < 0)
                {
                    var i = all.Count + nth.Value;
                    if (i >= 0 && i < all.Count)
                    {
                        output.Add(all[(int)i]);
                    }
                }
                else
                {
                    output.AddRange(all);
                }
            }
        }
        else if (startDay <= dim)
        {
            output.Add(new DateTime(y, m, startDay, 0, 0, 0, DateTimeKind.Unspecified));
        }

        return [.. output.Distinct().Order()];
    }

    private static List<DateTime> ApplySetPos(List<DateTime> v, IReadOnlyList<long> pos)
    {
        var sorted = v.Distinct().Order().ToList();
        if (pos.Count == 0)
        {
            return sorted;
        }

        long n = sorted.Count;
        return [.. pos.Select(p => p > 0 ? p - 1 : n + p).Where(i => i >= 0 && i < n).Select(i => sorted[(int)i]).Distinct().Order()];
    }

    /// <summary>The candidates of one period of the rule; null for a frequency that is not expanded.</summary>
    private static List<DateTime>? Candidates(DateTime start, TimeSpan t, RecurrenceRule r, long period)
    {
        var step = checked(period * r.Interval);
        switch (r.Freq)
        {
            case "daily":
            {
                var d = start.Date.AddDays(step);
                var ok = (r.ByDay.Count == 0 || r.ByDay.Any(w => w.Day == d.DayOfWeek))
                    && (r.ByMonth.Count == 0 || r.ByMonth.Contains(d.Month))
                    && (r.ByMonthDay.Count == 0 || r.ByMonthDay.Any(md => md == d.Day || md == d.Day - DateTime.DaysInMonth(d.Year, d.Month) - 1));
                return ok ? [d + t] : [];
            }

            case "weekly":
            {
                var off = (RecurrenceRule.MondayIndex(start.DayOfWeek) + 7 - RecurrenceRule.MondayIndex(r.FirstDayOfWeek)) % 7;
                var ws = start.Date.AddDays(-off).AddDays(checked(step * 7));
                var days = r.ByDay.Count == 0 ? [start.DayOfWeek] : r.ByDay.Select(w => w.Day).ToList();
                var v = Enumerable.Range(0, 7).Select(i => ws.AddDays(i)).Where(d => days.Contains(d.DayOfWeek) && (r.ByMonth.Count == 0 || r.ByMonth.Contains(d.Month))).Select(d => d + t).ToList();
                return ApplySetPos(v, r.BySetPos);
            }

            case "monthly":
            {
                var m0 = start.Month - 1 + step;
                var y = start.Year + (m0 / 12);
                var m = (int)(m0 % 12) + 1;
                if (y is < 1 or > 9999)
                {
                    throw new ArgumentOutOfRangeException(nameof(period));
                }

                return r.ByMonth.Count > 0 && !r.ByMonth.Contains(m) ? [] : ApplySetPos([.. MonthDays(r, (int)y, m, start.Day).Select(d => d + t)], r.BySetPos);
            }

            case "yearly":
                return YearlyCandidates(start, t, r, step);
            case "hourly":
                return [start.AddHours(step)];
            case "minutely":
                return [start.AddMinutes(step)];
            default:
                return null;
        }
    }

    private static List<DateTime> YearlyCandidates(DateTime start, TimeSpan t, RecurrenceRule r, long step)
    {
        var y = start.Year + step;
        if (y is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        var year = (int)y;
        var v = new List<DateTime>();
        if (r.ByMonth.Count == 0 && r.ByMonthDay.Count == 0 && r.ByDay.Any(d => d.Nth is not null))
        {
            // nth weekday of the year
            var all = Enumerable.Range(0, DateTime.IsLeapYear(year) ? 366 : 365).Select(i => new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified).AddDays(i)).ToList();
            foreach (var (w, nth) in r.ByDay.Select(x => (x.Day, x.Nth)))
            {
                var ds = all.Where(d => d.DayOfWeek == w).ToList();
                if (nth is { } n)
                {
                    var i = n > 0 ? n - 1 : ds.Count + n;
                    if (i >= 0 && i < ds.Count)
                    {
                        v.Add(ds[(int)i] + t);
                    }
                }
            }
        }
        else
        {
            IEnumerable<int> months = r.ByMonth.Count == 0 ? [start.Month] : r.ByMonth;
            foreach (var m in months)
            {
                v.AddRange(MonthDays(r, year, m, start.Day).Select(d => d + t));
            }
        }

        return ApplySetPos(v, r.BySetPos);
    }
}
