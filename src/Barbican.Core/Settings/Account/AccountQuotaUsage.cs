// Mirrors: Bulwark components/settings/account-settings.tsx (the storage row: "3.5 MB of 1 GB used", a bar), Core/Mail/Attachments/ByteSize.cs for the decimal units (extended to GB and TB here). G-057. Contract from the M6 plan section 4.5; owner set-data.

using System.Globalization;

namespace Barbican.Core.Settings.Account;

/// <summary>Used and total storage of an account.</summary>
public sealed record AccountQuotaUsage(long Used, long? Limit)
{
    /// <summary>Used over limit, clamped to 0..1; -1 without a limit.</summary>
    public double Fraction => Limit is not > 0 ? -1 : Math.Clamp((double)Math.Max(Used, 0) / Limit.Value, 0, 1);

    /// <summary>"3.5 MB of 1 GB used", or "3.5 MB used" without a limit.</summary>
    public string Text => Limit is > 0 ? $"{Size(Used)} of {Size(Limit.Value)} used" : $"{Size(Used)} used";

    private static string Size(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(bytes, 0);
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return value.ToString("0.#", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
