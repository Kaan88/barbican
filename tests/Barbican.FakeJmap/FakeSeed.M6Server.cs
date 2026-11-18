// The M6 srv-data seed: an out-of-office reply with a period, and a filters script written by the Bulwark-compatible generator (one rule). Synthetic data only. A test builds the server with FakeJmapOptions { VacationCapability = true, Vacation = FakeSeed.VacationM6(), SieveCapability = true },
// then FakeApi.SeedSieve("filters", FakeSeed.FiltersScriptM6(), true). Owner srv-data.

using Barbican.Core.Sieve;

namespace Barbican.FakeJmap;

public static partial class FakeSeed
{
    /// <summary>An enabled out-of-office reply from 20 to 24 December 2026.</summary>
    public static FakeVacation VacationM6() => new()
    {
        IsEnabled = true,
        FromDate = "2026-12-20T00:00:00Z",
        ToDate = "2026-12-24T23:59:59Z",
        Subject = "Away",
        TextBody = "I am away until the 25th.",
    };

    /// <summary>A Sieve script with one visual rule (news.example mail is filed in "News"); what the filters page writes.</summary>
    public static string FiltersScriptM6() => SieveScript.Generate([SieveScript.Rule("News", "from", "domain_is", "news.example", "move", "News", false)], string.Empty);
}
