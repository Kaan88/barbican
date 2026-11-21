// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:50-57,396-468 (EVENT_FIELDS, CREATE_FIELDS, UID_RE, FREQUENCIES, recurrenceOf, participantsOf) and the field checks of
// doCalendarCreateEvent / doCalendarUpdateEvent (:475-560); lib/plugin-types.ts:575-613 CalendarEventInput. Arbitrary JSCalendar objects are not accepted: unknown properties are refused.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>An event a plugin described, checked field by field (title 1-500, description 10,000, location 500, at most 50 participants, repeat, time).</summary>
internal sealed partial class PluginEventInput
{
    internal const int MaxTitle = 500;
    internal const int MaxDescription = 10_000;
    internal const int MaxLocation = 500;
    internal const int MaxParticipants = 50;
    private static readonly string[] Fields = ["title", "description", "start", "end", "duration", "timeZone", "isAllDay", "location", "calendarId", "participants", "repeat"];
    private static readonly string[] Frequencies = ["daily", "weekly", "monthly", "yearly"];
    private static readonly string[] RepeatFields = ["frequency", "interval", "count", "until"];
    private static readonly string[] PersonFields = ["email", "name"];

    internal string? Title { get; private set; }

    internal string? Description { get; private set; }

    internal string? Location { get; private set; }

    internal string? CalendarId { get; private set; }

    internal string? Uid { get; private set; }

    internal IReadOnlyList<CalendarPerson>? Participants { get; private set; }

    internal bool HasRepeat { get; private set; }

    /// <summary>The recurrence rule to store, or null to remove the repetition.</summary>
    internal JsonObject? Repeat { get; private set; }

    internal PluginTimingInput Timing { get; private set; } = new(null, null, null, null, null);

    /// <summary>Whether any of start, end, duration, timeZone or isAllDay was given.</summary>
    internal bool HasTiming => Timing.Start is not null || Timing.End is not null || Timing.Duration is not null || Timing.TimeZone is not null || Timing.IsAllDay is not null || TimeZoneGiven;

    internal bool TimeZoneGiven { get; private set; }

    internal bool IsEmpty { get; private set; }

    /// <summary>Checks the object; false for anything the Bulwark call throws on. An update's times are checked piece by piece here and worked out against the stored event when it runs.</summary>
    internal static bool TryParse(PluginValue? value, bool create, out PluginEventInput input)
    {
        input = new PluginEventInput();
        string[] allowed = create ? [.. Fields, "uid"] : Fields;
        if (!PluginMailArgs.TryOptions(value, allowed, true, out var o) || o is null) return false;
        input.IsEmpty = o.PropertyNames.All(n => PluginMailArgs.Prop(o, n) is null);
        if (input.IsEmpty && !create) return false;
        if (create && PluginMailArgs.Prop(o, "title") is null) return false;
        if (!ReadTexts(o, input) || !ReadUid(o, create, input) || !ReadCalendar(o, input) || !ReadParticipants(o, input) || !ReadRepeat(o, input)) return false;
        if (!ReadTiming(o, out var timing, out var zoneGiven)) return false;
        (input.Timing, input.TimeZoneGiven) = (timing, zoneGiven);
        // an update's times are worked out against the stored event; a new event's are complete now
        return !create || PluginTimingBuilder.TryBuild(timing, "UTC", out _);
    }

    private static bool ReadTexts(PluginValue o, PluginEventInput input)
    {
        if (PluginMailArgs.Prop(o, "title") is { } title)
        {
            if (!PluginMailArgs.TryString(title, 100_000, out var text) || text.Trim() is not { Length: > 0 and <= MaxTitle } trimmed) return false;
            input.Title = trimmed;
        }

        if (PluginMailArgs.Prop(o, "description") is { } description)
        {
            if (!PluginMailArgs.TryString(description, 100_000, out var text) || text.Trim() is not { Length: <= MaxDescription } trimmed) return false;
            input.Description = trimmed;
        }

        if (PluginMailArgs.Prop(o, "location") is { } location)
        {
            if (!PluginMailArgs.TryString(location, 100_000, out var text) || text.Trim() is not { Length: <= MaxLocation } trimmed) return false;
            input.Location = trimmed;
        }

        return true;
    }

    /// <summary>host-api-calendar.ts:401,483-487: 1-128 of A-Z a-z 0-9 . _ : - .</summary>
    private static bool ReadUid(PluginValue o, bool create, PluginEventInput input)
    {
        if (!create || PluginMailArgs.Prop(o, "uid") is not { } uid) return true;
        if (!PluginMailArgs.TryString(uid, 128, out var text) || !UidShape().IsMatch(text)) return false;
        input.Uid = text;
        return true;
    }

    private static bool ReadCalendar(PluginValue o, PluginEventInput input)
    {
        if (PluginMailArgs.Prop(o, "calendarId") is not { } id) return true;
        if (!PluginMailArgs.IsId(id)) return false;
        input.CalendarId = id.StringValue;
        return true;
    }

    /// <summary>host-api-calendar.ts:447-468: at most 50, each { email, name? } with an address-shaped email.</summary>
    private static bool ReadParticipants(PluginValue o, PluginEventInput input)
    {
        if (PluginMailArgs.Prop(o, "participants") is not { } list) return true;
        if (list.Kind != PluginValueKind.Array || list.Count > MaxParticipants) return false;
        var people = new List<CalendarPerson>(list.Count);
        for (var i = 0; i < list.Count; i++)
        {
            if (!PluginMailArgs.TryOptions(list.Item(i), PersonFields, true, out var p) || p is null) return false;
            if (!PluginMailArgs.TryString(PluginMailArgs.Prop(p, "email"), 1000, out var email) || email.Trim() is not { Length: >= 3 and <= 254 } address || !AddressShape().IsMatch(address)) return false;
            var name = "";
            if (PluginMailArgs.Prop(p, "name") is { } nameValue && (!PluginMailArgs.TryString(nameValue, 10_000, out name) || (name = name.Trim()).Length > 200)) return false;
            people.Add(new CalendarPerson(name, address));
        }

        input.Participants = people;
        return true;
    }

    /// <summary>host-api-calendar.ts:415-434.</summary>
    private static bool ReadRepeat(PluginValue o, PluginEventInput input)
    {
        if (!o.PropertyNames.Contains("repeat", StringComparer.Ordinal) || o.Property("repeat").Kind == PluginValueKind.Undefined) return true;
        input.HasRepeat = true;
        var repeat = o.Property("repeat");
        if (repeat.Kind == PluginValueKind.Null) return true;
        if (!PluginMailArgs.TryOptions(repeat, RepeatFields, true, out var r) || r is null) return false;
        if (!PluginMailArgs.TryString(PluginMailArgs.Prop(r, "frequency"), 16, out var frequency) || !Frequencies.Contains(frequency, StringComparer.Ordinal)) return false;
        var rule = new JsonObject { ["@type"] = "RecurrenceRule", ["frequency"] = frequency };
        if (!PluginMailArgs.TryInteger(PluginMailArgs.Prop(r, "interval"), 1, 99, 1, out var interval)) return false;
        if (PluginMailArgs.Prop(r, "interval") is not null && interval != 1) rule["interval"] = interval;
        var (count, until) = (PluginMailArgs.Prop(r, "count"), PluginMailArgs.Prop(r, "until"));
        if (count is not null && until is not null) return false;
        if (count is not null)
        {
            if (!PluginMailArgs.TryInteger(count, 1, 1000, 1, out var n)) return false;
            rule["count"] = n;
        }

        if (until is not null)
        {
            if (!PluginMailArgs.TryString(until, 10, out var day) || !PluginWhenParser.TryRead(day, out var when) || when.Time is not null || day.Length != 10) return false;
            rule["until"] = day + "T23:59:59";
        }

        input.Repeat = rule;
        return true;
    }

    private static bool ReadTiming(PluginValue o, out PluginTimingInput timing, out bool zoneGiven)
    {
        timing = new PluginTimingInput(null, null, null, null, null);
        zoneGiven = false;
        string? start = null, end = null, duration = null, zone = null;
        bool? allDay = null;
        if (PluginMailArgs.Prop(o, "start") is { } s) { if (!PluginMailArgs.TryString(s, 64, out start) || !PluginWhenParser.TryRead(start, out _)) return false; }
        if (PluginMailArgs.Prop(o, "end") is { } e) { if (!PluginMailArgs.TryString(e, 64, out end) || !PluginWhenParser.TryRead(end, out _)) return false; }
        if (PluginMailArgs.Prop(o, "duration") is { } d) { if (!PluginMailArgs.TryString(d, 64, out duration) || !PluginTimingBuilder.TryDuration(duration, out _)) return false; }
        if (PluginMailArgs.Prop(o, "timeZone") is { } z)
        {
            zoneGiven = true;
            if (z.Kind != PluginValueKind.Null && (!PluginMailArgs.TryString(z, 64, out zone) || !PluginTimingBuilder.IsKnownZone(zone))) return false;
        }

        if (PluginMailArgs.Prop(o, "isAllDay") is { } a)
        {
            if (a.Kind != PluginValueKind.Boolean) return false;
            allDay = a.BooleanValue;
        }

        if (end is not null && duration is not null) return false;
        timing = new PluginTimingInput(start, end, duration, zone, allDay);
        return true;
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,128}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex UidShape();

    [GeneratedRegex("^[^\\s@<>]+@[^\\s@<>]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex AddressShape();
}
