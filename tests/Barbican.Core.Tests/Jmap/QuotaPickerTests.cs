// Ported from Rust src/jmap.rs test quota_prefers_mail_account_octets (:1865).

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Quota;

namespace Barbican.Core.Tests.Jmap;

public sealed class QuotaPickerTests
{
    private static JsonObject Q(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    [RustTest("jmap::quota_prefers_mail_account_octets")]
    public void Quota_prefers_mail_account_octets()
    {
        List<JsonObject> list =
        [
            Q("""{"id": "1", "resourceType": "count", "scope": "account", "used": 1, "hardLimit": 9}"""),
            Q("""{"id": "2", "resourceType": "octets", "scope": "domain", "types": ["Mail"], "used": 1, "hardLimit": 10}"""),
            Q("""{"id": "3", "resourceType": "octets", "scope": "account", "types": ["Mail", "Calendar"], "used": 5, "hardLimit": 1000}"""),
            Q("""{"id": "4", "resourceType": "octets", "scope": "account", "types": ["Contact"], "used": 0, "hardLimit": 2}"""),
        ];
        Assert.Equal("3", QuotaPicker.Pick(list)!["id"]!.GetValue<string>());
        Assert.Equal("4", QuotaPicker.Pick(list.Skip(3).ToList())!["id"]!.GetValue<string>());
        Assert.Null(QuotaPicker.Pick(list.Take(1).ToList()));
    }
}
