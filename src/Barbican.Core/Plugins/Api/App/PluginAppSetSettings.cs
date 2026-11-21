// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.settings.ts:210-262 doSetSettings (settings:change, c.ts:34; mutates): 1 to 20 settings an object of name to value; each key is checked and written on its own (c.settings.ts:60-106
// settingRefusal), through the setter the settings pages use, then read back (another add-on's onBeforeSettingChange may have stopped it: refused "stopped"); resolves to { applied: { name: { old, new } }, refused: { name: reason },
// undoToken } where the token (30 minutes, this add-on, this login) undoes exactly the changes made. A value equal to the current one counts as applied and writes nothing.
// Allowed settings are the table in PluginSettingsTable: never credentials, server or administrator settings, add-on state or a security toggle; the delete and privacy safety nets only move toward safe (confirmDelete and
// confirmBulkDelete only on, permanentlyDeleteJunk, senderFavicons and showAvatarsInJunk only off).
// Deviations: 30 writes a minute per add-on (Bulwark has none); a name over 100 characters is InvalidArguments; the write goes through ISettingValues.SetAsync, so a refusal by Coerce reads "stopped" too. The stale checks follow
// the other writes: stale before a write writes nothing (StaleContext), during the call after a write reports UnknownOutcome.
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.App;

internal sealed class PluginAppSetSettings(PluginSettingsHost host) : PluginAppSettingsMethod("app.setSettings", "settings:change", host, mutates: true, rateKind: "settings-write", rateLimit: PluginSettingsHost.WritesPerMinute)
{
    private const int MaxKeyLength = 100;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.At(arguments, 0) is { Kind: PluginValueKind.Object } changes && changes.PropertyNames.Count is > 0 and <= PluginSettingsTable.MaxPerCall && changes.PropertyNames.All(k => k.Length <= MaxKeyLength)
            ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var changes = arguments.Item(0);
        var applied = new List<(string, int)>();
        var refused = new List<(string, int)>();
        var previous = new JsonObject();
        var b = new PluginValueBuilder();
        foreach (var name in changes.PropertyNames)
        {
            var refusal = PluginSettingChecks.Check(name, changes.Property(name), out var setting, out var value);
            if (refusal is { } why)
            {
                refused.Add((name, b.String(PluginSettingChecks.Wire(why))));
                continue;
            }

            var old = PluginSettingValues.Effective(Host.Values, setting!.Row, setting.PrefsKey);
            if (!PluginSettingValues.Same(old, value))
            {
                if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
                var written = await Host.Values.SetAsync(setting.PrefsKey, value, ct).ConfigureAwait(false);
                if (!written.IsOk || !PluginSettingValues.Same(PluginSettingValues.Effective(Host.Values, setting.Row, setting.PrefsKey), value))
                {
                    refused.Add((name, b.String(PluginSettingChecks.Wire(PluginSettingRefusal.Stopped))));
                    continue;
                }

                previous[setting.Name] = old?.DeepClone();
            }

            applied.Add((name, b.Object(("old", PluginSettingValues.Emit(b, old) ?? b.Null()), ("new", PluginSettingValues.Emit(b, value) ?? b.Null()))));
        }

        // the call may have gone stale while the hooks and the store worked: the token is only handed over for a call that is still current
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var token = previous.Count > 0 ? Host.Undo.Issue(context.Binding.Package.Id, capture!.LocalAccountKey, PluginSettingsHost.UndoKind, previous) : null;
        return Success(b, b.Object(("applied", b.Object([.. applied])), ("refused", b.Object([.. refused])), ("undoToken", token is null ? b.Null() : b.String(token))));
    }
}
