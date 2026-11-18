// Helpers of the M6 filter tests (Rust's json! macro and the test folder list). Owner srv-data; no other stream's test folder uses this name.

using System.Text.Json.Nodes;
using Barbican.Core.Filters.Rules;

namespace Barbican.Core.Tests.Filters.Rules;

internal static class FilterTestData
{
    public static JsonNode J(string json) => JsonNode.Parse(json)!;

    public static IReadOnlyList<FilterFolder> Folders() => [new("i", "INBOX"), new("n", "News/Letters")];

    public static FilterCond Cond(string field, string comparator, string value = "", string header = "") => new(field, comparator, value, header);

    public static FilterAct Act(string kind, string value = "", string mailbox = "", bool keepCopy = false) => new(kind, value, mailbox, keepCopy);
}
