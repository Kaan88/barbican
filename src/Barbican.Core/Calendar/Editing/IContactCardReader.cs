// Mirrors: Rust cal_extra.rs:33 fetch_people (ContactCard/query + get, read only: names, emails, anniversaries, groups); Bulwark lib/birthday-calendar.ts. Contacts proper are M5 (the contacts stream may replace the default). Contract from the M4 plan section 4.3; owner cal-edit.

using System.Text.Json.Nodes;
using Barbican.Core.Common;

namespace Barbican.Core.Calendar.Editing;

/// <summary>The contact cards the calendar reads for guest suggestions and birthdays. Empty without the contacts capability.</summary>
public interface IContactCardReader
{
    Task<Result<IReadOnlyList<JsonNode>>> CardsAsync(string accountKey, CancellationToken ct);
}
