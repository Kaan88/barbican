// Mirrors: Bulwark lib/calendar-editability.ts (an event with guests that the user does not organise is read-only: no drag, no resize); Rust cal_editor.rs organiser rules; M4 plan section 6.3 (gesture tests).

using System.Text.Json.Nodes;

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>Whether the user may move an event by dragging it.</summary>
public static class CalendarEditability
{
    /// <summary>
    /// An event without guests (or with the user as its only participant) is the user's own; one with guests is movable only when an owner participant is one of <paramref name="myAddresses"/>
    /// (a login or address, compared ignoring case), or when no participant is marked owner.
    /// </summary>
    public static bool CanMove(JsonNode ev, IReadOnlyList<string> myAddresses)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(myAddresses);
        if (ev["participants"] is not JsonObject participants || participants.Count <= 1)
        {
            return true;
        }

        var owners = participants.Select(p => p.Value).OfType<JsonObject>().Where(IsOwner).ToList();
        return owners.Count == 0 || owners.Any(o => AddressesOf(o).Any(a => myAddresses.Contains(a, StringComparer.OrdinalIgnoreCase)));
    }

    private static bool IsOwner(JsonObject participant) => participant["roles"] is JsonObject roles && roles["owner"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static IEnumerable<string> AddressesOf(JsonObject participant)
    {
        if (participant["email"] is JsonValue e && e.TryGetValue<string>(out var email) && email.Length > 0)
        {
            yield return email;
        }

        if (participant["sendTo"] is JsonObject send)
        {
            foreach (var target in send)
            {
                if (target.Value is JsonValue v && v.TryGetValue<string>(out var uri))
                {
                    yield return uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? uri[7..] : uri;
                }
            }
        }
    }
}
