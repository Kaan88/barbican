// Mirrors: Rust cal_editor.rs:90 event_organizer, :107 user_organizes, :112 attendees_of, :131 build_participant_map, :173 participant_fields, :192 asked_again; Bulwark lib/calendar-participants.ts (buildParticipantMap, participantFieldsToSave, participantsAskedAgain, userOrganizes).

using System.Text.Json.Nodes;

namespace Barbican.Core.Calendar.Editing;

public static partial class EventEditor
{
    /// <summary>The address of a participant: calendarAddress, else sendTo imip, else email, without "mailto:".</summary>
    internal static string ParticipantEmail(JsonNode? p)
    {
        var a = p.Str("calendarAddress") ?? p.Child("sendTo").Str("imip") ?? p.Str("email");
        return a is null ? "" : StripMailto(a.Trim());
    }

    private static string StripMailto(string address) => address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? address[7..] : address;

    private static bool IsMe(string email, IReadOnlyList<string> me) => me.Any(m => string.Equals(m, email.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool IsOwner(JsonNode? p) => p.Child("roles").IsTrue("owner");

    /// <summary>The owner participant or organizerCalendarAddress as a person.</summary>
    public static CalendarPerson? EventOrganizer(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        if (ev.Obj("participants") is { } participants)
        {
            var orgAddress = ev.Str("organizerCalendarAddress") is { } oa ? StripMailto(oa).ToLowerInvariant() : null;
            foreach (var (_, p) in participants)
            {
                var email = ParticipantEmail(p);
                if (email.Length > 0 && (IsOwner(p) || string.Equals(orgAddress, email.ToLowerInvariant(), StringComparison.Ordinal)))
                {
                    return new CalendarPerson(p.Str("name") ?? "", email);
                }
            }
        }

        var addr = ev.Str("organizerCalendarAddress") ?? ev.Child("replyTo").Str("imip");
        if (addr is null)
        {
            return null;
        }

        var organizer = StripMailto(addr.Trim());
        return organizer.Length > 0 ? new CalendarPerson("", organizer) : null;
    }

    /// <summary>The user organises: no organiser at all, an owner participant or organizerCalendarAddress among <paramref name="me"/> (case-insensitive), or isOrigin.</summary>
    public static bool UserOrganizes(JsonNode ev, IReadOnlyList<string> me)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(me);
        return EventOrganizer(ev) is not { } o || IsMe(o.Email, me) || ev.IsTrue("isOrigin");
    }

    /// <summary>The non-owner participants, deduplicated case-insensitively.</summary>
    public static IReadOnlyList<CalendarPerson> AttendeesOf(JsonNode ev)
    {
        ArgumentNullException.ThrowIfNull(ev);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (EventOrganizer(ev) is { } org)
        {
            seen.Add(org.Email.ToLowerInvariant());
        }

        var output = new List<CalendarPerson>();
        foreach (var (_, p) in ev.Obj("participants") ?? [])
        {
            var email = ParticipantEmail(p);
            var key = email.ToLowerInvariant();
            if (key.Length == 0 || IsOwner(p) || !seen.Add(key))
            {
                continue;
            }

            output.Add(new CalendarPerson(p.Str("name") ?? "", email));
        }

        return output;
    }

    /// <summary>The participants map with every guest but the organiser and the user set back to needs-action.</summary>
    public static JsonNode AskedAgain(JsonNode participants, IReadOnlyList<string> me)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(me);
        if (participants is not JsonObject ps)
        {
            return participants.DeepClone();
        }

        var output = new JsonObject();
        foreach (var (id, p) in ps)
        {
            var copy = p?.DeepClone();
            if (copy is JsonObject c && !IsOwner(c) && !IsMe(ParticipantEmail(c), me) && c.Str("participationStatus") is { } status && !string.Equals(status, "needs-action", StringComparison.Ordinal))
            {
                c["participationStatus"] = "needs-action";
            }

            output[id] = copy;
        }

        return output;
    }

    /// <summary>The participants a save writes (Bulwark buildParticipantMap): the organizer, then each attendee once. Known addresses keep their participant id and answer, unless the user as the organizer moved the event (<paramref name="askAgain"/>):
    /// then attendees answer anew, but the user's own stays.</summary>
    internal static JsonObject BuildParticipantMap(CalendarPerson organizer, IReadOnlyList<CalendarPerson> attendees, JsonNode? existing, bool askAgain, IReadOnlyList<string> me)
    {
        var known = new Dictionary<string, (string Id, string? Status)>(StringComparer.Ordinal);
        foreach (var (id, p) in existing as JsonObject ?? [])
        {
            var key = ParticipantEmail(p).ToLowerInvariant();
            if (key.Length == 0)
            {
                continue;
            }

            var status = p.Str("participationStatus");
            if (!known.TryGetValue(key, out var seen))
            {
                known[key] = (id, status);
            }
            else if ((seen.Status is null or "needs-action") && status is not null)
            {
                known[key] = (seen.Id, status);
            }
        }

        var output = new JsonObject();
        string IdFor(string key) => known.TryGetValue(key, out var k) && !output.ContainsKey(k.Id) ? k.Id : NewUid();
        var organizerKey = organizer.Email.Trim().ToLowerInvariant();
        var organizerStatus = known.TryGetValue(organizerKey, out var ok) && ok.Status is { } os && !string.Equals(os, "needs-action", StringComparison.Ordinal) ? os : "accepted";
        output[IdFor(organizerKey)] = Participant(organizer, new JsonObject { ["owner"] = true }, organizerStatus, false);
        var seenKeys = new HashSet<string>(StringComparer.Ordinal) { organizerKey };
        foreach (var a in attendees)
        {
            var email = a.Email.Trim();
            var key = email.ToLowerInvariant();
            if (key.Length == 0 || !seenKeys.Add(key))
            {
                continue;
            }

            var status = (askAgain && !IsMe(email, me) ? null : known.GetValueOrDefault(key).Status) ?? "needs-action";
            output[IdFor(key)] = Participant(a, new JsonObject { ["attendee"] = true }, status, true);
        }

        return output;
    }

    private static JsonObject Participant(CalendarPerson person, JsonObject roles, string status, bool expectReply)
    {
        var email = person.Email.Trim();
        var p = new JsonObject
        {
            ["@type"] = "Participant",
            ["email"] = email,
            ["calendarAddress"] = "mailto:" + email,
            ["roles"] = roles,
            ["participationStatus"] = status,
            ["scheduleAgent"] = "server",
            ["expectReply"] = expectReply,
            ["kind"] = "individual",
        };
        if (person.Name.Trim().Length > 0)
        {
            p["name"] = person.Name.Trim();
        }

        return p;
    }

    /// <summary>The participant fields of a save (Bulwark participantFieldsToSave).</summary>
    internal static JsonObject ParticipantFields(JsonNode? ev, IReadOnlyList<CalendarPerson> attendees, IReadOnlyList<string> me, bool rescheduled)
    {
        var fields = new JsonObject();
        if (attendees.Count > 0 && me.Count > 0)
        {
            var organizer = (ev is null ? null : EventOrganizer(ev)) ?? new CalendarPerson("", me[0]);
            var ask = rescheduled && (ev is null || UserOrganizes(ev, me));
            fields["participants"] = BuildParticipantMap(organizer, attendees, ev.Child("participants"), ask, me);
            if (ev is null || !ev.IsString("organizerCalendarAddress"))
            {
                fields["organizerCalendarAddress"] = "mailto:" + organizer.Email;
            }
        }
        else if (attendees.Count == 0 && ev is not null && ev.Obj("participants") is { Count: > 0 })
        {
            fields["participants"] = null;
            if (ev.Child("replyTo") is not null)
            {
                fields["replyTo"] = null;
            }

            if (ev.IsString("organizerCalendarAddress"))
            {
                fields["organizerCalendarAddress"] = null;
            }
        }

        return fields;
    }
}
