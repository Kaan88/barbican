// The fake account's vacation response (RFC 8621 section 8: one singleton, id "singleton": isEnabled, fromDate, toDate, subject, textBody, htmlBody; Stalwart crates/jmap/src/vacation/get.rs, set.rs; Bulwark lib/jmap/client.ts:5310-5347). A test hands one to FakeJmapOptions.Vacation; the arms are in FakeApi.Vacation.cs. M6 plan section 4.12. Owner srv-data.

namespace Barbican.FakeJmap;

/// <summary>The vacation response of the fake account.</summary>
public sealed record FakeVacation
{
    public bool IsEnabled { get; init; }

    public string? FromDate { get; init; }

    public string? ToDate { get; init; }

    public string? Subject { get; init; }

    public string? TextBody { get; init; }

    public string? HtmlBody { get; init; }
}
