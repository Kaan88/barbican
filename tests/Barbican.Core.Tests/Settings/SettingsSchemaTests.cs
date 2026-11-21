using System.Security.Cryptography;
using System.Text.Json;
using Barbican.Core.Settings;

namespace Barbican.Core.Tests.Settings;

public sealed class SettingsSchemaTests
{
    [Fact]
    public void Embedded_schema_matches_the_reviewed_catalog()
    {
        using var stream = SettingsSchema.OpenRaw();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        // A Windows checkout may carry CRLF if .gitattributes was bypassed; compare the LF form.
        var bytes = buffer.ToArray().Where(b => b != (byte)'\r').ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // Rust-final schema plus the Barbican row and reviewed corrections of extracted
        // dynamic/template choices (D-M6-A14), Bulwark style and folder-icon defaults, and the G-053 remote content rows
        // (externalContentPolicy, trustedSenders); LF-normalized so changes are deliberate.
        Assert.Equal(ReviewedSha256, hash);
    }

    [Fact]
    public void Schema_has_the_rust_tabs_and_rows()
    {
        using var stream = SettingsSchema.OpenRaw();
        using var doc = JsonDocument.Parse(stream);
        var tabs = doc.RootElement.GetProperty("tabs");

        Assert.Equal(21, tabs.GetArrayLength());
        Assert.Equal(280, tabs.EnumerateArray().Sum(t => t.GetProperty("rows").GetArrayLength()));
        Assert.Equal("account", tabs[0].GetProperty("id").GetString());
    }

    private const string ReviewedSha256 = "9cd6ba003fbb9f717235141af9724ea3788aac329a34dbff310c2d16db915fd2";
}
