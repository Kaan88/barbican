// Not Rust rows: G-021 (vacation audience, forward), Bulwark vacation-settings.tsx validation, vacation-audience.ts ownDomains. M6 stream srv-data.

using Barbican.Core.Settings.Vacation;

namespace Barbican.Core.Tests.Settings.Vacation;

public sealed class VacationRulesTests
{
    private static VacationSettings Base() => new(true, "Away", "I am away.", null, null, null, null, null, false);

    [Fact]
    [Red("srv-data")]
    public void A_vacation_is_checked_before_it_is_saved()
    {
        Assert.Null(VacationRules.Problem(Base()));
        Assert.Null(VacationRules.Problem(Base() with { Enabled = false, TextBody = string.Empty }));
        Assert.NotNull(VacationRules.Problem(Base() with { TextBody = string.Empty }));
        Assert.NotNull(VacationRules.Problem(Base() with { From = new DateOnly(2026, 12, 24), To = new DateOnly(2026, 12, 20) }));
        Assert.NotNull(VacationRules.Problem(Base() with { Audience = new VacationAudience("internal", []) }));
        Assert.NotNull(VacationRules.Problem(Base() with { Audience = new VacationAudience("external", ["not a host"]) }));
        Assert.Null(VacationRules.Problem(Base() with { Audience = new VacationAudience("external", ["example.org"]) }));
        Assert.NotNull(VacationRules.Problem(Base() with { Forward = new VacationForward(true, "nope", false) }));
        Assert.Null(VacationRules.Problem(Base() with { Forward = new VacationForward(true, "me@example.org", true) }));
    }

    [Fact]
    [Red("srv-data")]
    public void Own_domains_are_lower_case_unique_and_in_order()
    {
        Assert.Equal(["example.org", "example.com"], VacationRules.OwnDomains(["a@Example.org", "b@example.COM", "c@example.org", "broken", "d@localhost"]));
    }
}
