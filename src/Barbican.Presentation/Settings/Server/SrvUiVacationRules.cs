// Mirrors: Bulwark vacation-settings.tsx (the status line over the form). I-435, I-436. Contract from the M6 plan section 4.11; owner srv-ui.

using Barbican.Core.Common;
using Barbican.Core.Settings.Vacation;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The pure rules of the out-of-office page.</summary>
public static class SrvUiVacationRules
{
    /// <summary>The status line: "Off", "Active", "Active until 2026-12-24", "Starts 2026-12-20", "Ended 2026-12-24" (days are the settings' dates compared with <paramref name="today"/>; an enabled auto-reply with no dates is "Active").</summary>
    public static string Status(VacationSettings settings, DateOnly today) => throw new PortPendingException("SrvUiVacationRules.Status");
}
