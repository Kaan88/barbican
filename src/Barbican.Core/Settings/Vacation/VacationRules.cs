// Mirrors: Bulwark vacation-settings.tsx (validation), lib/sieve/period.ts, lib/sieve/vacation-audience.ts (DOMAIN, isValidVacationAudience, ownDomains), lib/sieve/vacation-forward.ts isValidVacationForward. Contract from the M6 plan section 4.7; owner srv-data.

using System.Text.RegularExpressions;

namespace Barbican.Core.Settings.Vacation;

/// <summary>The pure rules of the out-of-office page.</summary>
public static partial class VacationRules
{
    /// <summary>What is wrong with the settings before they are saved, or null: an enabled auto-reply needs a body; To must not be before From; an audience needs at least one domain, each a valid host name with a dot; a forward needs a valid address.</summary>
    public static string? Problem(VacationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Enabled && string.IsNullOrWhiteSpace(settings.TextBody) && string.IsNullOrWhiteSpace(settings.HtmlBody))
        {
            return "Write the message the auto-reply sends.";
        }

        if (settings.From is { } from && settings.To is { } to && to < from)
        {
            return "The out-of-office period ends before it starts.";
        }

        if (settings.Audience is { } audience)
        {
            if (audience.Only is not ("internal" or "external"))
            {
                return "Choose who gets the auto-reply.";
            }

            if (audience.Domains.Count == 0 || !audience.Domains.All(d => Domain().IsMatch(d)))
            {
                return "Enter your domains as host names, for example example.org.";
            }
        }

        if (settings.Forward is { } forward && (forward.Enabled || forward.To.Trim().Length > 0) && !ValidAddress(forward.To.Trim()))
        {
            return "Enter an address to forward to.";
        }

        return null;
    }

    /// <summary>The domains of the account's own addresses, lower-cased, each once, in order (Bulwark ownDomains).</summary>
    public static IReadOnlyList<string> OwnDomains(IReadOnlyList<string> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var domains = new List<string>();
        foreach (var address in addresses)
        {
            var at = address.LastIndexOf('@');
            var domain = at < 0 ? string.Empty : address[(at + 1)..].Trim().ToLowerInvariant();
            if (Domain().IsMatch(domain) && !domains.Contains(domain, StringComparer.Ordinal))
            {
                domains.Add(domain);
            }
        }

        return domains;
    }

    // a host name in the form the identities give it: labels of letters, digits and inner hyphens, at least two of them
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Domain();

    private static bool ValidAddress(string e)
    {
        var at = e.IndexOf('@', StringComparison.Ordinal);
        return at > 0 && e[(at + 1)..].Contains('.', StringComparison.Ordinal) && !e.Any(char.IsWhiteSpace) && !e.EndsWith('.') && !e[(at + 1)..].StartsWith('.');
    }
}
