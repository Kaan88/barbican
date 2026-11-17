// Mirrors: Rust glance.rs (greeting :27, title :40, gb :88, refresh :93 flagged names), Bulwark lib/quota.ts (fromQuota); spec B-MAIL-046. Owner mailcore.

using System.Globalization;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Glance;

/// <summary>The words and numbers of the Bento tiles and the list header; nothing is made up, everything comes from the account.</summary>
public static class GlanceRules
{
    /// <summary>"Good morning, Kaan." for the hour of the local clock (5 to 11 morning, 12 to 17 afternoon, else evening).</summary>
    public static string Greeting(int hour, string firstName)
    {
        ArgumentNullException.ThrowIfNull(firstName);
        var part = hour is >= 5 and <= 11 ? "Good morning" : hour is >= 12 and <= 17 ? "Good afternoon" : "Good evening";
        return firstName.Length == 0 ? part + "." : $"{part}, {firstName}.";
    }

    /// <summary>The first name from the first identity with a real display name (not empty, not an address).</summary>
    public static string FirstName(IReadOnlyList<Identity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var name = identities.Select(i => i.Name).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n) && !n.Contains('@', StringComparison.Ordinal));
        return name?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
    }

    /// <summary>The line under the folder title: the greeting and the unread count.</summary>
    public static string ListSubtitle(string greeting, int unread)
    {
        ArgumentNullException.ThrowIfNull(greeting);
        return unread switch
        {
            <= 0 => $"{greeting} Nothing unread here.",
            1 => $"{greeting} 1 unread message.",
            _ => string.Create(CultureInfo.InvariantCulture, $"{greeting} {unread} unread messages."),
        };
    }

    /// <summary>Gigabytes the way the storage meter writes them: whole from 10 GB, one decimal from 0.1 GB, else whole MB.</summary>
    public static string Gigabytes(long bytes)
    {
        var g = bytes / 1e9;
        if (g >= 10)
        {
            return g.ToString("0", CultureInfo.InvariantCulture) + " GB";
        }

        return g >= 0.1
            ? g.ToString("0.0", CultureInfo.InvariantCulture) + " GB"
            : Math.Round(bytes / 1e6, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " MB";
    }

    /// <summary>The storage meter text, "1.2 GB of 15 GB" or "3.4 GB used" without a limit; empty when the server reports no quota.</summary>
    public static string QuotaText(QuotaInfo? quota)
    {
        if (quota is null)
        {
            return string.Empty;
        }

        return quota.HardLimit > 0 ? $"{Gigabytes(quota.Used)} of {Gigabytes(quota.HardLimit)}" : $"{Gigabytes(quota.Used)} used";
    }

    /// <summary>How full the meter is, 0..1, or -1 when there is no limit to measure against.</summary>
    public static double QuotaFraction(QuotaInfo? quota) =>
        quota is { HardLimit: > 0 } ? Math.Clamp((double)quota.Used / quota.HardLimit, 0, 1) : -1;

    /// <summary>Who sent the latest flagged mail: up to two first names (or address local parts), newest first, no repeats.</summary>
    public static string FlaggedWho(IReadOnlyList<EmailSummary> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        var who = new List<string>();
        foreach (var email in newestFirst)
        {
            var from = email.From.Count > 0 ? email.From[0] : null;
            if (from is null)
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(from.Name) && !from.Name.Contains('@', StringComparison.Ordinal)
                ? from.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty
                : from.Email.Split('@')[0];
            if (name.Length > 0 && !who.Contains(name, StringComparer.Ordinal))
            {
                who.Add(name);
            }
        }

        return string.Join(", ", who.Take(2));
    }
}
