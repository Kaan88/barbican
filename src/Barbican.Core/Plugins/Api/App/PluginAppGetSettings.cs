// Mirrors: Bulwark lib/plugin-sandbox/host-api-app.ts:151-182 doAppGetSettings (settings:read, host-api.ts:206): the user's preferences that say how the app behaves for them, as plain values (strings, booleans, finite numbers) under the
// SHARED_SETTINGS allow-list, plus animationsEnabled (reduceMotion is not "on"), theme and calendarView. Nothing about other people, nothing that addresses a server, no credentials, no administrator or add-on state.
// Deviations: only the keys Barbican has a settings row for are reported (PluginSettingsTable); language, sessionTimeout (the organisation's idle timeout, not a user setting here) and the list-valued settings are absent;
// 60 reads a minute per add-on (Bulwark has none).
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.App;

internal sealed class PluginAppGetSettings(PluginSettingsHost host) : PluginAppSettingsMethod("app.getSettings", "settings:read", host, rateKind: "settings-read", rateLimit: PluginSettingsHost.ReadsPerMinute)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var b = new PluginValueBuilder();
        var fields = new List<(string, int)>();
        string? reduceMotion = null;
        foreach (var name in PluginSettingsTable.Shared)
        {
            if (PluginSettingsTable.ReadRow(name) is not { } row) continue;
            var value = PluginSettingValues.Effective(Host.Values, row, PluginSettingsTable.ReadPrefsKey(name));
            if (string.Equals(name, "reduceMotion", StringComparison.Ordinal) && value is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var motion)) reduceMotion = motion;
            if (PluginSettingValues.Emit(b, value) is { } id) fields.Add((name, id));
        }

        // add-ons written before "Reduce motion" read this switch
        fields.Add(("animationsEnabled", b.Boolean(!string.Equals(reduceMotion, "on", StringComparison.Ordinal))));
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        return Success(b, b.Object([.. fields]));
    }
}
