// Ported from Rust src/vcard.rs test :734 round_trip_and_folding (T-308); M5 stream con-data.

using Barbican.Core.Contacts;
using static Barbican.Core.Tests.Contacts.ContactTestData;

namespace Barbican.Core.Tests.Contacts;

public sealed class VCardWriterTests
{
    [Fact]
    [RustTest("vcard::round_trip_and_folding")]
    [Red("con-data")]
    public void Round_trip_and_folding()
    {
        var card = J("""
            {"uid": "urn:uuid:1234", "name": {"components": [{"kind": "given", "value": "Ünal"}, {"kind": "surname", "value": "Öztürk"}]},
             "emails": {"e0": {"address": "u@example.com", "contexts": {"work": true}, "pref": 1}},
             "phones": {"p0": {"number": "+90 555", "features": {"mobile": true}, "contexts": {"private": true}}},
             "addresses": {"a0": {"components": [{"kind": "name", "value": "Long Street"}, {"kind": "locality", "value": "Ankara"}], "contexts": {"private": true}}},
             "anniversaries": {"a": {"kind": "wedding", "date": {"year": 2001, "month": 6, "day": 9}}},
             "notes": {"n": {"note": "@@X200@@"}}, "keywords": {"Family": true},
             "relatedTo": {"Ayşe": {"relation": {"spouse": true}}}, "links": {"l": {"uri": "https://ex.org"}}}
            """.Replace("@@X200@@", new string('x', 200), StringComparison.Ordinal));
        var output = VCardWriter.Generate([card]);
        Assert.Contains("UID:urn:uuid:1234\r\n", output, StringComparison.Ordinal);
        Assert.Contains("EMAIL;TYPE=WORK;PREF=1:u@example.com", output, StringComparison.Ordinal);
        Assert.Contains("TEL;TYPE=HOME,CELL:+90 555", output, StringComparison.Ordinal);
        Assert.Contains("ANNIVERSARY:20010609", output, StringComparison.Ordinal);
        Assert.Contains("RELATED;VALUE=text;TYPE=spouse:Ayşe", output, StringComparison.Ordinal);
        Assert.All(output.Split("\r\n"), l => Assert.True(System.Text.Encoding.UTF8.GetByteCount(l) <= 75, l));

        var back = Assert.Single(VCardParser.Parse(output));
        Assert.Equal("urn:uuid:1234", (string?)back["uid"]);
        Assert.Equal(200, ((string?)First(back["notes"])["note"])!.Length);
        Assert.True((bool?)First(back["phones"])["features"]!["mobile"]);
        Assert.Equal(6, (int?)First(back["anniversaries"])["date"]!["month"]);
        Assert.True((bool?)back["relatedTo"]!["Ayşe"]!["relation"]!["spouse"]);
        Assert.True((bool?)back["keywords"]!["Family"]);
        Assert.Equal("Ankara", VCardParser.AddressPartsOf(First(back["addresses"])).Locality);
    }
}
