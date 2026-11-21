// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:61-68,97-100 (WhenParts, wallMs).
using System.Globalization;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>A time a plugin wrote: <c>YYYY-MM-DD</c>, <c>YYYY-MM-DDTHH:MM[:SS]</c> (wall clock) or the same followed by <c>Z</c> (UTC). Offsets like <c>+02:00</c> are refused (host-api-calendar.ts:72-75).</summary>
internal readonly record struct PluginWhen(string Date, string? Time, bool Utc)
{
    /// <summary>The wall clock as a date-time (a date alone is midnight).</summary>
    internal DateTime Wall => DateTime.ParseExact(Date + "T" + (Time ?? "00:00:00"), "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Wall-clock milliseconds, for differences only (host-api-calendar.ts:98).</summary>
    internal long WallMs => new DateTimeOffset(Wall, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
