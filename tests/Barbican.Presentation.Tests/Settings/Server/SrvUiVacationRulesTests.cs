// Not Rust rows: I-435, I-436 (Bulwark vacation-settings.tsx: the status over the form). M6 stream srv-ui.

using Barbican.Core.Settings.Vacation;
using Barbican.Presentation.Settings.Server;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiVacationRulesTests
{
    private static readonly DateOnly Today = new(2026, 12, 22);

    private static VacationSettings V(bool on, DateOnly? from, DateOnly? to) => new(on, "Away", "Away.", null, from, to, null, null, false);

    [Fact]
    [Red("srv-ui")]
    public void The_status_line_tells_off_active_scheduled_and_ended()
    {
        Assert.Equal("Off", SrvUiVacationRules.Status(V(false, null, null), Today));
        Assert.Equal("Active", SrvUiVacationRules.Status(V(true, null, null), Today));
        Assert.Equal("Active until 2026-12-24", SrvUiVacationRules.Status(V(true, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 24)), Today));
        Assert.Equal("Starts 2026-12-30", SrvUiVacationRules.Status(V(true, new DateOnly(2026, 12, 30), null), Today));
        Assert.Equal("Ended 2026-12-21", SrvUiVacationRules.Status(V(true, null, new DateOnly(2026, 12, 21)), Today));
    }
}
