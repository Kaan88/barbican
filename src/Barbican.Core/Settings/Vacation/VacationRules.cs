// Mirrors: Bulwark vacation-settings.tsx (validation), lib/sieve/period.ts, vacation-audience.ts isValidVacationAudience, vacation-forward.ts isValidVacationForward. Contract from the M6 plan section 4.7; owner srv-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Vacation;

/// <summary>The pure rules of the out-of-office page.</summary>
public static class VacationRules
{
    /// <summary>What is wrong with the settings before they are saved, or null: an enabled auto-reply needs a body; To must not be before From; an audience needs at least one domain, each a valid host name with a dot; a forward needs a valid address.</summary>
    public static string? Problem(VacationSettings settings) => throw new PortPendingException("VacationRules.Problem");

    /// <summary>The domains of the account's own addresses, lower-cased, each once, in order (Bulwark ownDomains).</summary>
    public static IReadOnlyList<string> OwnDomains(IReadOnlyList<string> addresses) => throw new PortPendingException("VacationRules.OwnDomains");
}
