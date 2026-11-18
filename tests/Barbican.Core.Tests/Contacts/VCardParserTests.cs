// Ported from Rust src/vcard.rs tests :684 parses_vcard3_with_types_and_apple_labels (T-306), :720 dates_reduced_forms (T-307, the reader and the writer half), :762 quoted_printable_and_skips_empty (T-309), :771 duplicates_in_file_and_existing (T-310); M5 stream con-data. Red until con-data lands the reader.
// The G-012 tests at the end are not Rust rows: the C# port adds limits the Rust parser never had (M5 plan D-M5-03).

#pragma warning disable MA0006, MA0002, CA1861 // ported Rust assertions compare string literals and literal arrays

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Contacts;
using static Barbican.Core.Tests.Contacts.ContactTestData;

namespace Barbican.Core.Tests.Contacts;

public sealed class VCardParserTests
{
    private static readonly Dictionary<string, string> NoParams = [];

    [Fact]
    [RustTest("vcard::parses_vcard3_with_types_and_apple_labels")]
    public void Parses_vcard3_with_types_and_apple_labels()
    {
        const string v = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Doe;Jane;Q;Dr.;Jr.\r\nFN:Dr. Jane Q Doe Jr.\r\nEMAIL;TYPE=INTERNET,WORK,PREF:jane@work.example\r\n"
            + "EMAIL;TYPE=HOME:jane@home.example\r\nTEL;TYPE=CELL:+1 555 0100\r\nitem1.TEL:+1 555 0199\r\nitem1.X-ABLabel:_$!<Ski cabin>!$_\r\n"
            + "ADR;TYPE=WORK:;;1 Main St;Springfield;IL;62701;USA\r\nBDAY:1985-04-12\r\nX-ABRELATEDNAMES;TYPE=pref:John Doe\r\nitem2.X-ABRELATEDNAMES:Ann\r\nitem2.X-ABLabel:_$!<Mother>!$_\r\n"
            + "URL;TYPE=WORK:https://example.com\r\nNOTE:Line one\\nline two\\, with comma\r\nCATEGORIES:Friends,VIP\r\nORG:ACME;R&D\r\nTITLE:Engineer\r\n"
            + "PHOTO;ENCODING=b;TYPE=JPEG:/9j/4AAQ\r\n SkZJRg==\r\nEND:VCARD\r\n";
        var cards = VCardParser.Parse(v);
        var c = Assert.Single(cards);
        Assert.Equal("Dr. Jane Q Doe Jr.", (string?)c["name"]!["full"]);
        var comps = c["name"]!["components"]!.AsArray();
        Assert.Equal("title", (string?)comps[0]!["kind"]);
        Assert.Equal("Jane", (string?)comps[1]!["value"]);
        Assert.Equal("Doe", (string?)comps[3]!["value"]);
        var emails = Values(c["emails"]);
        Assert.True((bool?)emails[0]["contexts"]!["work"]);
        Assert.Equal(1, (int?)emails[0]["pref"]);
        Assert.True((bool?)emails[1]["contexts"]!["private"]);
        var phones = Values(c["phones"]);
        Assert.True((bool?)phones[0]["features"]!["mobile"]);
        Assert.Equal("Ski cabin", (string?)phones[1]["label"]);
        Assert.Equal("Springfield", VCardParser.AddressPartsOf(First(c["addresses"])).Locality);
        var bday = First(c["anniversaries"]);
        Assert.Equal(1985, (int?)bday["date"]!["year"]);
        Assert.Equal(12, (int?)bday["date"]!["day"]);
        Assert.True((bool?)c["relatedTo"]!["Ann"]!["relation"]!["parent"]);
        Assert.IsType<JsonObject>(c["relatedTo"]!["John Doe"]);
        Assert.Equal("Line one\nline two, with comma", (string?)First(c["notes"])["note"]);
        Assert.True((bool?)c["keywords"]!["VIP"]);
        Assert.Equal("R&D", (string?)First(c["organizations"])["units"]![0]!["name"]);
        Assert.Equal("data:image/jpeg;base64,/9j/4AAQSkZJRg==", (string?)First(c["media"])["uri"]);
        Assert.Equal("https://example.com", (string?)First(c["links"])["uri"]);
    }

    [Fact]
    [RustTest("vcard::dates_reduced_forms")]
    public void Dates_reduced_forms()
    {
        Assert.Equal(4, (int?)VCardParser.ParseDate("--0412", NoParams)!["month"]);
        Assert.Null(VCardParser.ParseDate("--0412", NoParams)!["year"]);
        Assert.Equal(12, (int?)VCardParser.ParseDate("19850412T232050Z", NoParams)!["day"]);
        Assert.Equal(4, (int?)VCardParser.ParseDate("1985-04", NoParams)!["month"]);
        Assert.Equal(12, (int?)VCardParser.ParseDate("---12", NoParams)!["day"]);
        Assert.Null(VCardParser.ParseDate("1985-13-01", NoParams));
        Assert.Null(VCardParser.ParseDate("circa", NoParams));
        Assert.Equal("--0412", VCardWriter.DateToVCard(J("""{"month": 4, "day": 12}""")));
        Assert.Equal("19900102", VCardWriter.DateToVCard(J("""{"@type": "Timestamp", "utc": "1990-01-02T00:00:00Z"}""")));
    }

    [Fact]
    [RustTest("vcard::quoted_printable_and_skips_empty")]
    public void Quoted_printable_and_skips_empty()
    {
        const string v = "BEGIN:VCARD\nVERSION:2.1\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:M=C3=BCller;J=\n=C3=BCrgen\nEND:VCARD\nBEGIN:VCARD\nVERSION:3.0\nNOTE:nothing\nEND:VCARD\n";
        var c = Assert.Single(VCardParser.Parse(v));
        Assert.Equal("Jürgen", (string?)c["name"]!["components"]![0]!["value"]);
        Assert.Equal("Müller", (string?)c["name"]!["components"]![1]!["value"]);
    }

    [Fact]
    [RustTest("vcard::duplicates_in_file_and_existing")]
    public void Duplicates_in_file_and_existing()
    {
        var existing = new[] { J("""{"emails": {"a": {"address": "A@x.example"}}}""") };
        var incoming = new[] { J("""{"emails": {"a": {"address": "a@x.example"}}}"""), J("""{"emails": {"a": {"address": "b@x.example"}}}"""), J("""{"emails": {"a": {"address": "B@X.example "}}}""") };
        Assert.Equal(new[] { 0, 2 }, ContactImport.DetectDuplicates(existing, incoming));
        Assert.Equal(string.Empty, VCardParser.ParseDataUri("data:;base64,AAA")!.Value.MediaType);
        Assert.Equal("AAA", VCardParser.ParseDataUri("data:;base64,AAA")!.Value.Data);
        Assert.Equal("image/png", VCardParser.ParseDataUri("data:image/png;base64,AAA")!.Value.MediaType);
    }

    [Fact]
    public void An_oversized_photo_is_dropped_and_the_card_stays()
    {
        var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Ann Lee\r\nPHOTO;ENCODING=b;TYPE=JPEG:" + new string('A', 5000) + "\r\nEND:VCARD\r\n";
        var r = VCardParser.ParseChecked(vcf, new VCardLimits { MaxPhotoChars = 100 });
        Assert.True(r.IsOk);
        var card = Assert.Single(r.Value!.Cards);
        Assert.Equal("Ann Lee", (string?)card["name"]!["full"]);
        Assert.Null(card["media"]);
        Assert.Equal(1, r.Value.PhotosDropped);
    }

    [Fact]
    public void A_file_over_the_limit_is_refused_and_extra_cards_are_counted()
    {
        Assert.False(VCardParser.ParseChecked(new string('x', 2000), new VCardLimits { MaxFileChars = 1000 }).IsOk);
        var many = new StringBuilder();
        for (var i = 0; i < 10; i++)
        {
            _ = many.Append("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:P").Append(i).Append("\r\nEND:VCARD\r\n");
        }

        var r = VCardParser.ParseChecked(many.ToString(), new VCardLimits { MaxCards = 3 });
        Assert.True(r.IsOk);
        Assert.Equal(3, r.Value!.Cards.Count);
        Assert.Equal(7, r.Value.SkippedCards);
    }

    [Fact]
    public async Task Hostile_shapes_end_quickly_without_throwing()
    {
        var hostile = new[]
        {
            string.Concat(Enumerable.Repeat("BEGIN:VCARD\r\n", 20_000)),
            "BEGIN:VCARD\r\nFN:" + string.Concat(Enumerable.Repeat("a\r\n ", 50_000)) + "\r\nEND:VCARD\r\n",
            "BEGIN:VCARD\r\nN;ENCODING=QUOTED-PRINTABLE:=ZZ=\r\n=\r\n=C3\r\nEND:VCARD\r\n",
            "BEGIN:VCARD\r\nBDAY:99999-99-99\r\nPHOTO:data:image/png;base64,!!!!\r\nEND:VCARD\r\n",
            new string('\0', 10_000),
        };
        foreach (var text in hostile)
        {
            var parsed = await Task.Run(() => VCardParser.ParseChecked(text, new VCardLimits()), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(parsed.IsOk || parsed.Error is not null);
        }
    }
}
