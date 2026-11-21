// Mirrors: Bulwark lib/phishing-report.ts (isReportable :48-66, reportSubject :69, reportText :75, reporterIdentity :81); the report itself is
// PhishingReporter. Owner reader.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Safety;

/// <summary>The pure rules of "Report phishing".</summary>
public static class PhishingReportRules
{
    // folders whose messages are the user's own: nothing to report there (lib/phishing-report.ts:49)
    private static readonly HashSet<string> OwnMailRoles = new(["sent", "drafts", "scheduled"], StringComparer.Ordinal);

    /// <summary>
    /// Whether a message can be reported from a folder of <paramref name="mailboxRole"/>. <paramref name="folderRoles"/> are the roles of the folders the message
    /// itself is in (search results and All mail also show the user's own sent mail).
    /// </summary>
    public static bool IsReportable(Keywords keywords, string? mailboxRole, IEnumerable<string?> folderRoles)
    {
        ArgumentNullException.ThrowIfNull(keywords);
        ArgumentNullException.ThrowIfNull(folderRoles);
        if (mailboxRole is not null && OwnMailRoles.Contains(mailboxRole))
        {
            return false;
        }

        if (folderRoles.Any(role => role is not null && OwnMailRoles.Contains(role)))
        {
            return false;
        }

        // not a draft, and not reported already (it is in Junk with its links off)
        return !keywords.IsSet("$draft") && !PhishingLinks.IsMarked(keywords);
    }

    /// <summary>The subject of the report: Microsoft's reporting mailboxes read "Phishing:" as the verdict.</summary>
    public static string Subject(string? subject)
    {
        var clean = string.Join(' ', (subject ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return clean.Length == 0 ? "Phishing" : "Phishing: " + clean;
    }

    /// <summary>The short text of a forwarded report (English: the security team reads it, not the user).</summary>
    public static string Text(string? reporterName, string reporterEmail)
    {
        ArgumentNullException.ThrowIfNull(reporterEmail);
        var who = string.IsNullOrWhiteSpace(reporterName) ? reporterEmail : $"{reporterName.Trim()} <{reporterEmail}>";
        return $"Reported as phishing by {who} with Barbican.\r\nThe reported message is attached unchanged.\r\n";
    }

    /// <summary>The identity a report goes out from: the login's own address first, else the first one without a wildcard.</summary>
    public static Identity? ReporterIdentity(IEnumerable<Identity> identities, string username)
    {
        ArgumentNullException.ThrowIfNull(identities);
        var usable = identities.Where(i => !string.IsNullOrWhiteSpace(i.Email) && !i.Email.Contains('*', StringComparison.Ordinal)).ToList();
        return usable.FirstOrDefault(i => string.Equals(i.Email, username, StringComparison.OrdinalIgnoreCase)) ?? usable.FirstOrDefault();
    }
}
