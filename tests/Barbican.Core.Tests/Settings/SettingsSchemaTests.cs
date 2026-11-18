using System.Security.Cryptography;
using System.Text.Json;
using Barbican.Core.Settings;

namespace Barbican.Core.Tests.Settings;

public sealed class SettingsSchemaTests
{
    [Fact]
    public void Embedded_schema_is_the_verbatim_rust_copy()
    {
        using var stream = SettingsSchema.OpenRaw();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        // A Windows checkout may carry CRLF if .gitattributes was bypassed; compare the LF form.
        var bytes = buffer.ToArray().Where(b => b != (byte)'\r').ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        // SHA-256 of the Rust app's ui/settings-schema.json at tag rust-final (LF line endings) plus the one Barbican row
        // webviewBackgroundCloseMinutes (general tab, D-WV-01); the Rust file's own hash was 81ff2398dd34422869b978166be809b77af9f8afc853ecdd25ec2c5c5e2ad4c0.
        // A change here must be deliberate: the schema mirrors Bulwark's settings pages.
        Assert.Equal(RustFinalSha256, hash);
    }

    [Fact]
    public void Schema_has_the_rust_tabs_and_rows()
    {
        using var stream = SettingsSchema.OpenRaw();
        using var doc = JsonDocument.Parse(stream);
        var tabs = doc.RootElement.GetProperty("tabs");

        Assert.Equal(21, tabs.GetArrayLength());
        Assert.Equal(276, tabs.EnumerateArray().Sum(t => t.GetProperty("rows").GetArrayLength()));
        Assert.Equal("account", tabs[0].GetProperty("id").GetString());
    }

    private const string RustFinalSha256 = "0a85a7478136978abefa78e79fdf84cc8ba715aeaf0c7342dcb3f9bf21d6a62d";
}
