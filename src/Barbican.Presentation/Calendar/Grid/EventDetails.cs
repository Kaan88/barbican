// Mirrors: Rust calendar.rs:1250 detail (the context panel beside the grid: when, where, repeats, guests with their answers); Bulwark components/calendar/event-detail-popover.tsx; design BNCalendar "Event details" aside. I-331, B-CAL-054.

using Barbican.Core.Calendar;
using System.Text.Json.Nodes;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Builds <see cref="EventDetail"/> from an occurrence and its JSCalendar object (pure).</summary>
public static class EventDetails
{
    public static EventDetail From(CalendarOccurrence occurrence, JsonNode? ev, CalendarListRow? calendar, bool twelveHour)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        var guests = new List<EventGuest>();
        if (ev?["participants"] is JsonObject participants)
        {
            foreach (var p in participants.Select(x => x.Value).OfType<JsonObject>())
            {
                var email = Str(p, "email");
                if (email.Length == 0 && p["sendTo"] is JsonObject send && send.Select(s => s.Value).OfType<JsonValue>().FirstOrDefault() is { } first && first.TryGetValue<string>(out var uri))
                {
                    email = uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? uri[7..] : uri;
                }

                var name = Str(p, "name") is { Length: > 0 } n ? n : email;
                var owner = p["roles"] is JsonObject roles && roles["owner"] is JsonValue o && o.TryGetValue<bool>(out var isOwner) && isOwner;
                guests.Add(new EventGuest(name, Initials(name), owner ? "Organizer" : StatusText(Str(p, "participationStatus"))));
            }
        }

        var when = occurrence.Kind == CalendarOccurrenceKind.Task ? "Due " + CalendarFormat.When(occurrence.Start, occurrence.End, true, twelveHour) : CalendarFormat.When(occurrence.Start, occurrence.End, occurrence.AllDay, twelveHour);
        var title = occurrence.Kind == CalendarOccurrenceKind.Task ? AgendaRules.TaskTitle(occurrence.Title) : occurrence.Title;
        var description = ev is null ? string.Empty : PlainText(Str(ev, "description"));
        var repeats = ev?["recurrenceRules"] is JsonArray { Count: > 0 } || occurrence.RecurrenceId is not null;
        return new EventDetail(occurrence.Id, title, when, occurrence.Location, description, calendar?.Name ?? string.Empty, calendar?.Color ?? "#3b82f6", repeats, occurrence.Done, occurrence.Kind, guests);
    }

    /// <summary>The two letters of an avatar: the first letters of the first two words.</summary>
    public static string Initials(string name)
    {
        var words = (name ?? string.Empty).Split([' ', '@', '.'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "?" : string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
    }

    private static string StatusText(string status) => status switch
    {
        "accepted" => "Accepted",
        "declined" => "Declined",
        "tentative" => "Maybe",
        _ => "No reply",
    };

    private static string Str(JsonNode node, string key) => node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    /// <summary>The text of a description that may hold HTML: tags dropped, line breaks kept.</summary>
    public static string PlainText(string text)
    {
        if (text.IndexOf('<', StringComparison.Ordinal) < 0)
        {
            return text.Trim();
        }

        var sb = new System.Text.StringBuilder(text.Length);
        var inTag = false;
        foreach (var c in text)
        {
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>' && inTag)
            {
                inTag = false;
            }
            else if (!inTag)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }
}
