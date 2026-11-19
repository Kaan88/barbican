// Mirrors Bulwark lib/time-zone-choices.ts TIME_ZONE_ABBREVIATIONS.
namespace Barbican.Core.Settings.Pages;

/// <summary>Search aliases; values remain the original IANA IDs.</summary>
public static class SettingTimeZoneSearchAliases
{
    private static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["UTC"] = ["UTC", "Etc/UTC"],
        ["GMT"] = ["UTC", "Etc/UTC", "Europe/London", "Europe/Dublin"],
        ["BST"] = ["Europe/London", "Europe/Dublin"],
        ["IST"] = ["Asia/Kolkata", "Asia/Calcutta"],
        ["CET"] = ["Europe/Berlin", "Europe/Paris", "Europe/Madrid", "Europe/Rome", "Europe/Amsterdam", "Europe/Brussels", "Europe/Vienna", "Europe/Zurich", "Europe/Stockholm", "Europe/Oslo", "Europe/Copenhagen", "Europe/Warsaw", "Europe/Prague", "Europe/Budapest", "Europe/Belgrade", "Europe/Luxembourg"],
        ["CEST"] = ["Europe/Berlin", "Europe/Paris", "Europe/Madrid", "Europe/Rome", "Europe/Amsterdam", "Europe/Brussels", "Europe/Vienna", "Europe/Zurich", "Europe/Stockholm", "Europe/Oslo", "Europe/Copenhagen", "Europe/Warsaw", "Europe/Prague", "Europe/Budapest", "Europe/Belgrade", "Europe/Luxembourg"],
        ["MEZ"] = ["Europe/Berlin", "Europe/Paris", "Europe/Madrid", "Europe/Rome", "Europe/Amsterdam", "Europe/Brussels", "Europe/Vienna", "Europe/Zurich", "Europe/Stockholm", "Europe/Oslo", "Europe/Copenhagen", "Europe/Warsaw", "Europe/Prague", "Europe/Budapest", "Europe/Belgrade", "Europe/Luxembourg"],
        ["MESZ"] = ["Europe/Berlin", "Europe/Paris", "Europe/Madrid", "Europe/Rome", "Europe/Amsterdam", "Europe/Brussels", "Europe/Vienna", "Europe/Zurich", "Europe/Stockholm", "Europe/Oslo", "Europe/Copenhagen", "Europe/Warsaw", "Europe/Prague", "Europe/Budapest", "Europe/Belgrade", "Europe/Luxembourg"],
        ["EET"] = ["Europe/Athens", "Europe/Helsinki", "Europe/Kyiv", "Europe/Kiev", "Europe/Bucharest", "Europe/Sofia", "Europe/Riga", "Europe/Tallinn", "Europe/Vilnius", "Africa/Cairo"],
        ["EEST"] = ["Europe/Athens", "Europe/Helsinki", "Europe/Kyiv", "Europe/Kiev", "Europe/Bucharest", "Europe/Sofia", "Europe/Riga", "Europe/Tallinn", "Europe/Vilnius", "Africa/Cairo"],
        ["WET"] = ["Europe/Lisbon", "Atlantic/Canary", "Atlantic/Madeira", "Atlantic/Faroe", "Atlantic/Faeroe"],
        ["WEST"] = ["Europe/Lisbon", "Atlantic/Canary", "Atlantic/Madeira", "Atlantic/Faroe", "Atlantic/Faeroe"],
        ["MSK"] = ["Europe/Moscow"],
        ["EST"] = ["America/New_York", "America/Toronto", "America/Detroit", "America/Indiana/Indianapolis"],
        ["EDT"] = ["America/New_York", "America/Toronto", "America/Detroit", "America/Indiana/Indianapolis"],
        ["ET"] = ["America/New_York", "America/Toronto", "America/Detroit", "America/Indiana/Indianapolis"],
        ["CST"] = ["America/Chicago", "America/Winnipeg", "America/Mexico_City", "Asia/Shanghai"],
        ["CDT"] = ["America/Chicago", "America/Winnipeg", "America/Mexico_City"],
        ["CT"] = ["America/Chicago", "America/Winnipeg", "America/Mexico_City"],
        ["MST"] = ["America/Denver", "America/Phoenix", "America/Edmonton", "America/Boise"],
        ["MDT"] = ["America/Denver", "America/Phoenix", "America/Edmonton", "America/Boise"],
        ["MT"] = ["America/Denver", "America/Phoenix", "America/Edmonton", "America/Boise"],
        ["PST"] = ["America/Los_Angeles", "America/Vancouver", "America/Tijuana"],
        ["PDT"] = ["America/Los_Angeles", "America/Vancouver", "America/Tijuana"],
        ["PT"] = ["America/Los_Angeles", "America/Vancouver", "America/Tijuana"],
        ["AKST"] = ["America/Anchorage"],
        ["AKDT"] = ["America/Anchorage"],
        ["HST"] = ["Pacific/Honolulu"],
        ["JST"] = ["Asia/Tokyo"],
        ["KST"] = ["Asia/Seoul"],
        ["HKT"] = ["Asia/Hong_Kong"],
        ["SGT"] = ["Asia/Singapore"],
        ["AEST"] = ["Australia/Sydney", "Australia/Melbourne", "Australia/Brisbane", "Australia/Hobart", "Australia/Canberra"],
        ["AEDT"] = ["Australia/Sydney", "Australia/Melbourne", "Australia/Brisbane", "Australia/Hobart", "Australia/Canberra"],
        ["ACST"] = ["Australia/Adelaide", "Australia/Darwin"],
        ["ACDT"] = ["Australia/Adelaide"],
        ["AWST"] = ["Australia/Perth"],
        ["NZST"] = ["Pacific/Auckland"],
        ["NZDT"] = ["Pacific/Auckland"],
        ["BRT"] = ["America/Sao_Paulo"],
        ["ART"] = ["America/Argentina/Buenos_Aires", "America/Buenos_Aires"],
        ["SAST"] = ["Africa/Johannesburg"],
        ["WAT"] = ["Africa/Lagos"],
        ["EAT"] = ["Africa/Nairobi"],
        ["PKT"] = ["Asia/Karachi"],
        ["WIB"] = ["Asia/Jakarta"],
        ["ICT"] = ["Asia/Bangkok", "Asia/Ho_Chi_Minh", "Asia/Saigon"],
        ["PHT"] = ["Asia/Manila"],
        ["GST"] = ["Asia/Dubai"],
    };

    public static bool Matches(string query, string id) =>
        Aliases.TryGetValue(query, out var zones) && zones.Contains(id, StringComparer.OrdinalIgnoreCase);
}

