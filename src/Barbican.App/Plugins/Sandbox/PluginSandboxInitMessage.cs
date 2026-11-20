// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:797-845, plugin-i18n.ts:14-46, runtime.tsx:1480-1492.
using System.Globalization;
using System.Text.Json;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Protocol.Messages;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Projects trusted startup state into the sandbox protocol.</summary>
public static class PluginSandboxInitMessage
{
    private static readonly PluginValueLimits Limits = new(MaxWireBytes: 40 * 1024 * 1024, MaxBinaryBytes: 25 * 1024 * 1024, MaxStringBytes: 50 * 1024 * 1024);
    private static readonly PluginMessageLimits MessageLimits = new(MaxStringLength: 25 * 1024 * 1024, MaxGraphStringLength: 25 * 1024 * 1024, MaxGraphBinaryBytes: 25 * 1024 * 1024);

    public static PluginValue Create(PluginRuntimeStart request, string verifiedCode)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(verifiedCode);
        var package = request.Installation.Package;
        var grant = request.Grant;
        if (!request.Installation.Enabled || !grant.Enabled || grant.Package != package.Identity)
            throw new ArgumentException("Startup package and grant must be enabled and match.", nameof(request));
        var b = new PluginValueBuilder();
        int Json(JsonElement element) => b.Import(PluginValue.FromJson(element, Limits));
        var manifest = new List<(string, int)>
        {
            ("id", b.String(package.Identity.Id)), ("version", b.String(package.Identity.Version)),
            ("permissions", b.Array([.. grant.Permissions.Order(StringComparer.Ordinal).Select(b.String)])),
            ("settings", Json(request.HostState.Settings)), ("locales", LocaleTables(b, package.Manifest, request.HostState.Locale)),
        };
        if (package.Manifest.TryGetProperty("httpOrigins", out var origins)) manifest.Add(("httpOrigins", Json(origins)));
        var payload = new List<(string, int)>
        {
            ("mode", b.String(request.Kind == PluginFrameKind.Background ? "background" : "slot")),
            ("pluginId", b.String(package.Identity.Id)), ("tier", b.String(grant.Tier.ToString().ToLowerInvariant())),
            ("manifest", b.Object([.. manifest])), ("code", b.String(verifiedCode)), ("locale", b.String(request.HostState.Locale)),
            ("regional", Json(request.HostState.Regional)), ("hostFeatures", Json(request.HostState.Features)),
        };
        if (request.Kind == PluginFrameKind.Slot)
        {
            payload.Add(("slot", b.String(request.Slot ?? throw new ArgumentException("Slot is required.", nameof(request)))));
            payload.Add(("extraProps", b.Import(request.Props)));
            payload.Add(("theme", Json(request.HostState.Theme)));
        }
        else if (request.Kind != PluginFrameKind.Background || request.Slot is not null)
            throw new ArgumentException("Invalid frame kind or slot.", nameof(request));
        var message = b.Build(b.Object(("type", b.String("init")), ("payload", b.Object([.. payload]))), Limits);
        if (!PluginMessageValidator.Validate(message, PluginMessageDirection.HostToSandbox, MessageLimits))
            throw new ArgumentException("Invalid projected startup state.", nameof(request));
        return message;
    }

    private static int LocaleTables(PluginValueBuilder b, JsonElement manifest, string locale)
    {
        var selected = new Dictionary<string, int>(StringComparer.Ordinal);
        if (manifest.TryGetProperty("locales", out var tables) && tables.ValueKind == JsonValueKind.Object)
        {
            var parts = locale.Split('-');
            var chain = Enumerable.Range(1, parts.Length).Reverse().Select(i => string.Join('-', parts.Take(i))).Append("en");
            foreach (var candidate in chain.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (tables.TryGetProperty(candidate, out var exact))
                {
                    selected[candidate] = b.Import(PluginValue.FromJson(exact, Limits));
                    continue;
                }
                foreach (var table in tables.EnumerateObject())
                {
                    if (!string.Equals(table.Name, candidate, StringComparison.OrdinalIgnoreCase)) continue;
                    selected[table.Name] = b.Import(PluginValue.FromJson(table.Value, Limits));
                    break;
                }
            }
        }
        return b.Object([.. selected.Select(x => (x.Key, x.Value))]);
    }

    public static PluginActivation ParseActivation(PluginValue message)
    {
        if (!PluginMessageValidator.Validate(message, PluginMessageDirection.SandboxToHost, MessageLimits) ||
            !string.Equals(message.Property("type").StringValue, "init-done", StringComparison.Ordinal))
            throw new ArgumentException("Expected a valid init-done message.", nameof(message));
        static IEnumerable<PluginValue> Items(PluginValue array) => Enumerable.Range(0, array.Count).Select(array.Item);
        static bool OptionalBool(PluginValue value, string name) => value.PropertyNames.Contains(name, StringComparer.Ordinal) && value.Property(name).Kind == PluginValueKind.Boolean && value.Property(name).BooleanValue;
        static string? OptionalString(PluginValue value, string name) => value.PropertyNames.Contains(name, StringComparer.Ordinal) && value.Property(name).Kind == PluginValueKind.String ? value.Property(name).StringValue : null;
        return new PluginActivation(
            [.. Items(message.Property("hooks")).Select(x => x.StringValue)],
            [.. Items(message.Property("slots")).Select(x => new PluginSlotRegistration(x.Property("name").StringValue, x.Property("hasShouldShow").BooleanValue, double.Parse(x.Property("order").NumberText, CultureInfo.InvariantCulture), OptionalBool(x, "fill"), OptionalBool(x, "startFolded")))],
            [.. Items(message.Property("shortcuts")).Select(x => new PluginShortcut(x.Property("id").StringValue, x.Property("keys").StringValue, x.Property("label").StringValue, OptionalString(x, "category")))]);
    }
}
