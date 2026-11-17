// Mirrors: Rust mail.rs:1064 archive_path (the local year and, for "month", the two-digit month inside it); Bulwark lib/jmap/client.ts archiveEmails :4215
// (year-and-month folders under the Archive folder, from the message's receivedAt in local time); settings key archiveMode (single | year | month).

using System.Globalization;

namespace Barbican.Core.Mail.Archive;

/// <summary>The folder names under Archive a message goes to, by the archiveMode setting.</summary>
public static class ArchivePath
{
    /// <summary>Empty for "single" (and any unknown mode); the year, or the year and the month, of the receive time in <paramref name="zone"/>.</summary>
    public static IReadOnlyList<string> Of(DateTimeOffset received, string mode, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(received, zone);
        var year = local.Year.ToString("D4", CultureInfo.InvariantCulture);
        return mode switch
        {
            "year" => [year],
            "month" => [year, local.Month.ToString("D2", CultureInfo.InvariantCulture)],
            _ => [],
        };
    }
}
