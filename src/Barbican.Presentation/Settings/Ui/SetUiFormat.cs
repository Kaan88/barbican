// Mirrors: Bulwark components/settings/account-settings.tsx (the storage line "3.5 MB of 1 GB used"). Own formatter because AccountQuotaUsage.Text belongs to set-data (D-M6-B04). Owner set-ui.

using System.Globalization;

namespace Barbican.Presentation.Settings.Ui;

/// <summary>Text helpers of the settings pages.</summary>
public static class SetUiFormat
{
    /// <summary>"3.5 MB", "1 GB", "512 B".</summary>
    public static string Bytes(long bytes)
    {
        double v = Math.Max(0, bytes);
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var i = 0;
        while (v >= 1024 && i < units.Length - 1)
        {
            v /= 1024;
            i++;
        }

        return (i == 0 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture)) + " " + units[i];
    }

    /// <summary>"3.5 MB of 1 GB used", or "3.5 MB used" without a limit.</summary>
    public static string Quota(long used, long? limit) => limit is { } l and > 0 ? $"{Bytes(used)} of {Bytes(l)} used" : $"{Bytes(used)} used";

    /// <summary>Used over limit, 0..1; 0 without a limit.</summary>
    public static double QuotaFraction(long used, long? limit) => limit is { } l and > 0 ? Math.Clamp((double)used / l, 0, 1) : 0;
}
