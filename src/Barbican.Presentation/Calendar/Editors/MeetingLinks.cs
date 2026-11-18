// Mirrors: Bulwark event-modal.tsx virtualLocation, Rust cal_editor.rs set_call (a web address as the location is the video call link). Owner cal-ui.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The link a location or virtual location carries for joining a video call (Bulwark's virtual location, Rust's set_call).</summary>
public static partial class MeetingLinks
{
    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 200)]
    private static partial Regex Web();

    /// <summary>The location when it is a web address, else the event's first virtual location address.</summary>
    public static string? Find(string location, JsonNode? ev)
    {
        ArgumentNullException.ThrowIfNull(location);
        var trimmed = location.Trim();
        if (Web().IsMatch(trimmed))
        {
            return trimmed;
        }

        if (ev?["virtualLocations"] is JsonObject vls)
        {
            foreach (var (_, v) in vls)
            {
                if (CalendarJson.Str(v, "uri") is { Length: > 0 } uri && Web().IsMatch(uri))
                {
                    return uri;
                }
            }
        }

        return null;
    }
}
