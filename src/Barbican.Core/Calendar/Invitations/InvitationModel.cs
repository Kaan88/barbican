// Mirrors: Rust invite.rs:16 Method::from, :36 find_part, :49 method_from_ics, :57 norm_addr, :64 participant_email, :76 organizer, :88 my_participant,
// :96 i_organize, :100 looks_like_reply, :107 method, :118 duration_secs, :141 when, :163 location, :168 status_label, :250 trust; RFC 5546 (iTIP),
// RFC 8984 (JSCalendar); Bulwark lib/calendar-invitation.ts (findCalendarAttachment, getInvitationMethod, getInvitationTrustAssessment),
// components/email/calendar-invitation-banner.tsx; Stalwart crates/jmap/src/calendar_event/parse.rs (the server parses the .ics, J-072).
// Owner reader (display and trust only; Accept/Maybe/Decline and add to calendar are M4).

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.AuthResults;

namespace Barbican.Core.Calendar.Invitations;

/// <summary>The pure parts of the invitation banner.</summary>
public static class InvitationModel
{
    private const string DayFormat = "ddd dd MMM yyyy";

    /// <summary>The message's calendar part: an attachment first (text/calendar, application/ics, .ics, .ical), then a body part.</summary>
    public static JsonNode? FindPart(JsonNode message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var attachment = (message["attachments"] as JsonArray ?? []).FirstOrDefault(p => p is not null && IsCalendarPart(p) && p["blobId"] is JsonValue);
        return attachment ?? Walk(message["textBody"]) ?? Walk(message["htmlBody"]);
    }

    /// <summary>The METHOD line of raw iCalendar text; METHODICAL and the like are Unknown.</summary>
    public static InviteMethod MethodFromIcs(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        foreach (var line in raw.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.Length < 6 || !l[..6].Equals("METHOD", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = l[6..];
            var colon = rest.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || !(rest.StartsWith(':') || rest.StartsWith(';')))
            {
                continue;
            }

            var token = new string([.. rest[(colon + 1)..].Trim().TakeWhile(c => char.IsAsciiLetter(c) || c == '-')]);
            if (token.Length > 0)
            {
                return FromName(token);
            }
        }

        return InviteMethod.Unknown;
    }

    /// <summary>The method: the event's own method first, then the ics METHOD, then status cancelled, then a reply-shaped participant list, then Request when someone organises it.</summary>
    public static InviteMethod Method(JsonNode ev, string? rawIcs)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var m = FromName(ev.Str("method") ?? string.Empty);
        if (m != InviteMethod.Unknown)
        {
            return m;
        }

        if (rawIcs is not null && MethodFromIcs(rawIcs) is var fromIcs && fromIcs != InviteMethod.Unknown)
        {
            return fromIcs;
        }

        if (string.Equals(ev.Str("status"), "cancelled", StringComparison.Ordinal))
        {
            return InviteMethod.Cancel;
        }

        if (LooksLikeReply(ev))
        {
            return InviteMethod.Reply;
        }

        return Participants(ev).Any(p => IsOrganizer(p.Value)) ? InviteMethod.Request : InviteMethod.Unknown;
    }

    /// <summary>The organizer's name and address (calendarAddress without mailto:, organizerCalendarAddress for Stalwart), or null.</summary>
    public static InviteOrganizer? Organizer(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var ps = Participants(ev);
        var orgAddr = ev.Str("organizerCalendarAddress");
        var p = ps.FirstOrDefault(x => IsOrganizer(x.Value)).Value
            ?? (orgAddr is null ? null : ps.FirstOrDefault(x => string.Equals(x.Value.Str("calendarAddress"), orgAddr, StringComparison.Ordinal)).Value);
        if (p is not null)
        {
            var e = ParticipantEmail(p) ?? string.Empty;
            return new InviteOrganizer(p.Str("name") is { Length: > 0 } n ? n : e, e);
        }

        return orgAddr is null ? null : new InviteOrganizer(NormAddr(orgAddr), NormAddr(orgAddr));
    }

    /// <summary>The participant entry for one of the user's addresses; an organizer who also attends answers; an owner-only entry is not one.</summary>
    public static InviteParticipant? MyParticipant(JsonNode ev, IReadOnlyList<string> mine)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(mine);
        var lower = mine.Select(m => m.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, p) in Participants(ev))
        {
            if (ParticipantEmail(p) is { } e && lower.Contains(e) && !(IsOrganizer(p) && !Role(p, "attendee")))
            {
                return new InviteParticipant(id, p.Str("participationStatus") ?? "needs-action");
            }
        }

        return null;
    }

    /// <summary>Whether one of the user's addresses (case-insensitive) is the organizer.</summary>
    public static bool IOrganize(JsonNode ev, IReadOnlyList<string> mine)
    {
        ArgumentNullException.ThrowIfNull(mine);
        return Organizer(ev) is { } o && mine.Any(m => string.Equals(m, o.Email, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Seconds of an ISO 8601 duration (PT1H30M, P1DT2H, P2W), or null for an invalid one.</summary>
    public static long? DurationSeconds(string duration)
    {
        ArgumentNullException.ThrowIfNull(duration);
        var d = duration.StartsWith('-') ? duration[1..] : duration;
        if (!d.StartsWith('P'))
        {
            return null;
        }

        var rest = d[1..];
        var t = rest.IndexOf('T', StringComparison.Ordinal);
        var (date, time) = t >= 0 ? (rest[..t], rest[(t + 1)..]) : (rest, string.Empty);
        return Sum(date, c => c switch { 'W' => 604_800, 'D' => 86_400, _ => null }) is { } a
            && Sum(time, c => c switch { 'H' => 3600, 'M' => 60, 'S' => 1, _ => null }) is { } b
            ? a + b
            : null;
    }

    /// <summary>"Tue 17 Nov 2026, 10:00 – 11:30" in the event's zone, with the zone name when it differs from the local one; all-day events say so.</summary>
    public static string When(JsonNode ev, string localZone) => When(ev, localZone, TimeZoneInfo.Local);

    /// <summary>The same with the zone that UTC events are shown in (Rust: the local zone).</summary>
    public static string When(JsonNode ev, string localZone, TimeZoneInfo local)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(local);
        var raw = ev.Str("start");
        if (raw is null || !DateTime.TryParseExact(raw.Length >= 19 ? raw[..19] : raw, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            return string.Empty;
        }

        var dur = ev.Str("duration") is { } ds ? DurationSeconds(ds) ?? 0 : 0;
        var allDay = ev["showWithoutTime"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        var tz = ev.Str("timeZone") ?? string.Empty;
        DateTime s;
        var zone = string.Empty;
        if (tz.Equals("utc", StringComparison.OrdinalIgnoreCase) || tz.Equals("etc/utc", StringComparison.OrdinalIgnoreCase) || raw.EndsWith('Z'))
        {
            s = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(start, DateTimeKind.Utc), local);
        }
        else
        {
            s = start;
            zone = tz.Length == 0 || string.Equals(tz, localZone, StringComparison.Ordinal) ? string.Empty : $" ({tz})";
        }

        var e = s.AddSeconds(dur);
        if (allDay)
        {
            var last = dur >= 86_400 ? e.AddDays(-1) : s;
            return last.Date > s.Date ? $"{Day(s)} – {Day(last)}" : $"{Day(s)}, all day";
        }

        if (dur == 0)
        {
            return $"{Day(s)}, {Clock(s)}{zone}";
        }

        return e.Date == s.Date
            ? $"{Day(s)}, {Clock(s)} – {Clock(e)}{zone}"
            : $"{Day(s)}, {Clock(s)} – {e.ToString("ddd dd MMM", CultureInfo.InvariantCulture)}, {Clock(e)}{zone}";
    }

    /// <summary>The first location's name (or its description), or empty.</summary>
    public static string Location(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        foreach (var (_, l) in ev["locations"] as JsonObject ?? [])
        {
            var s = l.Str("name") ?? l.Str("description");
            if (s is not null && s.Trim().Length > 0)
            {
                return s.Trim();
            }
        }

        return string.Empty;
    }

    /// <summary>A display label for an event status ("tentative" gives "Tentative").</summary>
    public static string StatusLabel(string status) => status switch
    {
        "accepted" => "Accepted",
        "tentative" => "Tentative",
        "declined" => "Declined",
        "delegated" => "Delegated",
        _ => "Needs response",
    };

    /// <summary>
    /// How far the message can be trusted to come from who the calendar data names: a warning, a caution, or null when trusted.
    /// <paramref name="auth"/> is the account server's own Authentication-Results (AuthTrace, R2-07, D-9), <paramref name="hasAuth"/> whether there
    /// was one, <paramref name="mine"/> the account's own addresses (mail between them is internal).
    /// </summary>
    public static InviteTrust? Trust(JsonNode ev, InviteMethod method, string? from, IReadOnlyList<AuthResult> auth, bool hasAuth, IReadOnlyList<string> mine)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(mine);
        var sender = from is null ? null : NormAddr(from) is { Length: > 0 } n ? n : null;
        var fromAttendee = method is InviteMethod.Reply or InviteMethod.Counter or InviteMethod.Refresh;
        var expectedRaw = fromAttendee
            ? Participants(ev).Select(p => p.Value).FirstOrDefault(p => !Role(p, "owner") && p.Str("participationStatus") is { } st && !string.Equals(st, "needs-action", StringComparison.Ordinal)) is { } a ? ParticipantEmail(a) : null
            : Organizer(ev)?.Email is { Length: > 0 } oe ? oe : null;
        var expected = expectedRaw is null ? null : NormAddr(expectedRaw);
        var verified = sender is not null && AuthenticationResults.AlignedPass(auth, sender);
        var failed = AuthenticationResults.Failure(auth, verified);
        var mismatch = sender is not null && expected is not null && !string.Equals(sender, expected, StringComparison.Ordinal);
        var sd = sender is null ? null : AuthenticationResults.DomainOf(sender);
        var internalMail = !hasAuth && sd is not null && mine.Any(m => string.Equals(AuthenticationResults.DomainOf(m), sd, StringComparison.Ordinal));
        var (s, e) = (sender ?? string.Empty, expected ?? string.Empty);
        var mismatchText = fromAttendee
            ? $"This response was sent from {s}, while the attendee answering in the calendar data is {e}"
            : $"This invitation was sent from {s}, while the organizer listed in the calendar data is {e}";
        if (mismatch && (failed || !verified))
        {
            return new InviteTrust(true, $"{mismatchText}, and the message could not be verified.");
        }

        if (failed)
        {
            return new InviteTrust(true, "Mail authentication checks for this invitation failed. Treat calendar actions with caution.");
        }

        if (mismatch)
        {
            return new InviteTrust(false, $"{mismatchText}.");
        }

        if (method != InviteMethod.Unknown && !internalMail && !verified)
        {
            return new InviteTrust(false, "This invitation does not include verified mail authentication. Confirm the details with the organizer if anything looks unusual.");
        }

        return null;
    }

    /// <summary>A participant's address: email, calendarAddress (Stalwart) or a sendTo value; lower-case without mailto:.</summary>
    public static string? ParticipantEmail(JsonNode p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Str("email") is { } e && NormAddr(e) is { Length: > 0 } ne)
        {
            return ne;
        }

        if (p.Str("calendarAddress") is { } c && NormAddr(c) is { Length: > 0 } nc)
        {
            return nc;
        }

        return (p["sendTo"] as JsonObject ?? []).Select(x => x.Value.AsString()).OfType<string>().Select(NormAddr).FirstOrDefault(x => x.Length > 0);
    }

    /// <summary>The method named by an iTIP token (case-insensitive), Unknown for anything else.</summary>
    public static InviteMethod FromName(string name) => name.Trim().ToLowerInvariant() switch
    {
        "publish" => InviteMethod.Publish,
        "request" => InviteMethod.Request,
        "reply" => InviteMethod.Reply,
        "add" => InviteMethod.Add,
        "cancel" => InviteMethod.Cancel,
        "refresh" => InviteMethod.Refresh,
        "counter" => InviteMethod.Counter,
        "declinecounter" => InviteMethod.DeclineCounter,
        _ => InviteMethod.Unknown,
    };

    private static string Day(DateTime t) => t.ToString(DayFormat, CultureInfo.InvariantCulture);

    private static string Clock(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static long? Sum(string part, Func<char, long?> unit)
    {
        long total = 0;
        var num = string.Empty;
        foreach (var c in part)
        {
            if (char.IsAsciiDigit(c))
            {
                num += c;
                continue;
            }

            if (!long.TryParse(num, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || unit(c) is not { } u)
            {
                return null;
            }

            num = string.Empty;
            total += n * u;
        }

        return total;
    }

    private static string NormAddr(string v)
    {
        var t = v.Trim();
        return (t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? t[7..] : t).ToLowerInvariant();
    }

    private static List<KeyValuePair<string, JsonNode>> Participants(JsonNode ev) =>
        [.. (ev["participants"] as JsonObject ?? []).Where(x => x.Value is not null).Select(x => new KeyValuePair<string, JsonNode>(x.Key, x.Value!))];

    private static bool Role(JsonNode p, string role) => p["roles"]?[role] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static bool IsOrganizer(JsonNode p) => Role(p, "owner") || Role(p, "chair");

    private static bool LooksLikeReply(JsonNode ev) =>
        Participants(ev).Any(x => Role(x.Value, "attendee") && !IsOrganizer(x.Value)
            && ((x.Value.Str("participationStatus") is { } s && !string.Equals(s, "needs-action", StringComparison.Ordinal)) || x.Value["participationComment"] is JsonValue));

    private static bool IsCalendarType(string t)
    {
        var m = t.Split(';')[0].Trim().ToLowerInvariant();
        return m is "text/calendar" or "application/ics" or "application/icalendar";
    }

    private static bool IsCalendarPart(JsonNode p)
    {
        var name = (p.Str("name") ?? string.Empty).ToLowerInvariant();
        return IsCalendarType(p.Str("type") ?? string.Empty) || name.EndsWith(".ics", StringComparison.Ordinal) || name.EndsWith(".ical", StringComparison.Ordinal);
    }

    private static JsonNode? Walk(JsonNode? parts)
    {
        foreach (var p in parts as JsonArray ?? [])
        {
            if (p is null)
            {
                continue;
            }

            if (IsCalendarPart(p) && p["blobId"] is JsonValue)
            {
                return p;
            }

            if (Walk(p["subParts"]) is { } n)
            {
                return n;
            }
        }

        return null;
    }
}
