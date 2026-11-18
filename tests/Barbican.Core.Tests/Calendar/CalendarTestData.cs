// Helpers of the M4 calendar tests (Rust's dt() and json!).

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Tests.Calendar;

internal static class CalendarTestData
{
    /// <summary>A wall-clock time from "yyyy-MM-ddTHH:mm:ss".</summary>
    public static DateTime Dt(string text) => DateTime.ParseExact(text, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    public static DateOnly D(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static JsonNode J(string json) => JsonNode.Parse(json)!;

    public static JsonObject O(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>UTC milliseconds of a wall time read as UTC (independent of the zone code under test).</summary>
    public static long UtcMs(string text) => new DateTimeOffset(Dt(text), TimeSpan.Zero).ToUnixTimeMilliseconds();

    /// <summary>UTC milliseconds of a wall time on this machine's clock.</summary>
    public static long LocalMs(string text)
    {
        var t = Dt(text);
        return new DateTimeOffset(t, TimeZoneInfo.Local.GetUtcOffset(t)).ToUnixTimeMilliseconds();
    }
}
