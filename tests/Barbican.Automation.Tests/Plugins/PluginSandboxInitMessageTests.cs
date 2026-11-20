using System.Text.Json;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxInitMessageTests
{
    private static PluginRuntimeStart Start(PluginFrameKind kind)
    {
        using var manifest = JsonDocument.Parse("""{"locales":{"de":{"greeting":"Hallo"},"en":{"greeting":"Hello"},"fr":{"greeting":"Bonjour"}},"httpOrigins":["https://example.org"],"secret":"must-not-appear"}""");
        using var regional = JsonDocument.Parse("""{"locale":"de-CH","dateLocale":"de-CH","hour12":false,"timeZone":"UTC","firstDayOfWeek":1}""");
        using var theme = JsonDocument.Parse("""{"dark":false,"fontFamily":"sans","vars":{}}""");
        using var settings = JsonDocument.Parse("""{"enabled":true}""");
        using var features = JsonDocument.Parse("""{"externalSenderLabel":false,"externalRecipientWarning":true,"noThirdPartyTraffic":false}""");
        var identity = new PluginPackageIdentity("fixture", "1.0.0", new string('a', 64));
        var grant = new PluginGrantSnapshot(identity, 1, true, ["storage:read"], ["private-account"]);
        var package = new PluginPackageDefinition(identity, PluginRuntimeKind.JavaScript, "index.js", manifest.RootElement, ["storage:read", "mail:read"], PluginPackageSource.LocalImport);
        var b = new PluginValueBuilder();
        return new(new(package, true, grant), grant, "auto-8931", kind, kind == PluginFrameKind.Slot ? "sidebar-widget" : null,
            b.Build(b.Object(("example", b.String("value")))), new("de-CH", regional.RootElement, theme.RootElement, settings.RootElement, features.RootElement));
    }

    [Fact]
    public void Background_projection_limits_manifest_and_uses_granted_permissions()
    {
        var payload = PluginSandboxInitMessage.Create(Start(PluginFrameKind.Background), "verified code").Property("payload");
        var manifest = payload.Property("manifest");
        Assert.Equal("verified code", payload.Property("code").StringValue);
        Assert.Equal("storage:read", manifest.Property("permissions").Item(0).StringValue);
        Assert.Equal(1, manifest.Property("permissions").Count);
        Assert.DoesNotContain("secret", manifest.PropertyNames, StringComparer.Ordinal);
        Assert.DoesNotContain("slot", payload.PropertyNames, StringComparer.Ordinal);
        Assert.DoesNotContain("theme", payload.PropertyNames, StringComparer.Ordinal);
        Assert.Equal(["de", "en"], manifest.Property("locales").PropertyNames);
        Assert.Equal("https://example.org", manifest.Property("httpOrigins").Item(0).StringValue);
    }

    [Fact]
    public void Slot_projection_includes_props_and_theme_without_inventing_fill()
    {
        var payload = PluginSandboxInitMessage.Create(Start(PluginFrameKind.Slot), "code").Property("payload");
        Assert.Equal("sidebar-widget", payload.Property("slot").StringValue);
        Assert.Equal("value", payload.Property("extraProps").Property("example").StringValue);
        Assert.False(payload.Property("theme").Property("dark").BooleanValue);
        Assert.DoesNotContain("fill", payload.PropertyNames, StringComparer.Ordinal);
    }

    [Fact]
    public void Activation_preserves_registrations_and_rejects_other_messages()
    {
        using var json = JsonDocument.Parse("""{"type":"init-done","hooks":["mail:send"],"slots":[{"name":"sidebar-widget","hasShouldShow":true,"order":1.5,"fill":true}],"shortcuts":[{"id":"open","keys":"Ctrl+O","label":"Open","category":"Mail"}]}""");
        var activation = PluginSandboxInitMessage.ParseActivation(PluginValue.FromJson(json.RootElement));
        Assert.Equal("mail:send", Assert.Single(activation.Hooks));
        var slot = Assert.Single(activation.Slots);
        Assert.True(slot.Fill); Assert.False(slot.StartFolded); Assert.Equal(1.5, slot.Order);
        Assert.Equal("Mail", Assert.Single(activation.Shortcuts).Category);
        using var invalid = JsonDocument.Parse("""{"type":"init-error","error":"failed"}""");
        Assert.Throws<ArgumentException>(() => PluginSandboxInitMessage.ParseActivation(PluginValue.FromJson(invalid.RootElement)));
    }
}
