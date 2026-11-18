// G-012 / L-034 in unit-test form (no account, no network): hostile vCard and photo shapes the Rust parser never had limits for (M5 plan D-M5-03). The three tests in VCardParserTests pin the limits; these add more shapes.

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text;
using Barbican.Core.Contacts;
using static Barbican.Core.Tests.Contacts.ContactTestData;

namespace Barbican.Core.Tests.Contacts;

public sealed class VCardHostileInputTests
{
    [Fact]
    public void A_line_over_the_limit_is_cut_not_refused()
    {
        var vcf = "BEGIN:VCARD\r\nFN:Ann Lee\r\nNOTE:" + new string('n', 5000) + "\r\nEND:VCARD\r\n";
        var r = VCardParser.ParseChecked(vcf, new VCardLimits { MaxLineChars = 1000 });
        var note = (string?)First(Assert.Single(r.Value!.Cards)["notes"])["note"];
        Assert.True(note!.Length < 1000);
        Assert.Equal(0, r.Value.PhotosDropped);
    }

    [Fact]
    public void A_nested_begin_without_end_drops_the_unfinished_card_and_never_recurses()
    {
        var vcf = "BEGIN:VCARD\r\nFN:Lost\r\nBEGIN:VCARD\r\nFN:Kept\r\nEND:VCARD\r\n";
        var card = Assert.Single(VCardParser.Parse(vcf));
        Assert.Equal("Kept", (string?)card["name"]!["full"]);
    }

    [Fact]
    public void Ten_thousand_entries_in_one_card_are_capped_per_kind()
    {
        var sb = new StringBuilder("BEGIN:VCARD\r\nFN:Many\r\n");
        for (var i = 0; i < 10_000; i++)
        {
            _ = sb.Append("EMAIL:m").Append(i).Append("@x.example\r\nCATEGORIES:c").Append(i).Append("\r\n");
        }

        _ = sb.Append("END:VCARD\r\n");
        var r = VCardParser.ParseChecked(sb.ToString(), new VCardLimits());
        var card = Assert.Single(r.Value!.Cards);
        Assert.Equal(500, card["emails"]!.AsObject().Count);
        Assert.Equal(500, card["keywords"]!.AsObject().Count);
    }

    [Fact]
    public void A_lone_carriage_return_ends_a_line_and_nul_characters_are_dropped()
    {
        var card = Assert.Single(VCardParser.Parse("BEGIN:VCARD\rFN:Ann\0 Lee\rEMAIL:a@x.example\rEND:VCARD\r"));
        Assert.Equal("Ann Lee", (string?)card["name"]!["full"]);
        Assert.Equal("a@x.example", (string?)First(card["emails"])["address"]);
    }

    [Fact]
    public void A_photo_that_is_not_base64_is_not_kept()
    {
        var card = Assert.Single(VCardParser.Parse("BEGIN:VCARD\r\nFN:Ann Lee\r\nPHOTO:data:image/png;base64,!!!!\r\nPHOTO:data:text/html,<script>\r\nEND:VCARD\r\n"));
        Assert.Null(card["media"]);
    }

    [Fact]
    public void A_stray_line_break_in_a_value_cannot_forge_a_property_on_export()
    {
        var card = J("""{"name": {"full": "Ann"}, "emails": {"e0": {"address": "a@x.example\r\nEND:VCARD\r\nBEGIN:VCARD\r\nFN:Forged"}}}""");
        var output = VCardWriter.Generate([card]);
        var lines = output.Split("\r\n");
        Assert.Equal(1, lines.Count(l => l == "BEGIN:VCARD"));
        Assert.Equal(1, lines.Count(l => l == "END:VCARD"));
        Assert.DoesNotContain(lines, l => l.StartsWith("FN:Forged", StringComparison.Ordinal));
    }

    [Fact]
    public void A_picture_with_a_huge_declared_size_is_refused_before_decoding()
    {
        // a PNG header declaring 100 000 x 100 000 pixels with no image data behind it
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
            0, 1, 0x86, 0xA0, 0, 1, 0x86, 0xA0, 8, 2, 0, 0, 0, 0x7F, 0x2D, 0xA3, 0x57,
        ];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(ContactPhoto.Shrink(png).IsOk);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void An_embedded_picture_over_the_cap_is_refused_on_its_text_and_bad_base64_is_refused()
    {
        Assert.False(ContactPhoto.FromDataUri("data:image/png;base64," + new string('A', (ContactPhoto.MaxInputBytes / 3 * 4) + 100)).IsOk);
        Assert.False(ContactPhoto.FromDataUri("data:image/png;base64,!!!!").IsOk);
        Assert.False(ContactPhoto.FromDataUri("https://x.example/a.png").IsOk);
        var ok = ContactPhoto.FromDataUri("data:image/png;base64,AAAA");
        Assert.True(ok.IsOk);
        Assert.Equal(3, ok.Value.Bytes.Length);
    }
}
