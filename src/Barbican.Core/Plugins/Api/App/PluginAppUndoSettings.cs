// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.settings.ts:264-300 doUndoSettings (settings:change, c.ts:36; mutates) over c.undo.ts:46-65 withUndoToken: puts back what app.setSettings changed, the exact old value, setting by setting;
// resolves to { restored: [name], refused: { name: reason } }. A token is only good for the add-on that got it, in the same login, for 30 minutes and once; an unknown, spent, expired or foreign token is Unavailable. The old value
// needs no longer be one the table offers (the user had it), but the setting must still be allowed and available, and the token is spent after the loop (the refused ones are listed, as in Bulwark). 30 writes a minute per add-on.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.App;

internal sealed class PluginAppUndoSettings(PluginSettingsHost host) : PluginAppSettingsMethod("app.undoSettings", "settings:change", host, mutates: true, rateKind: "settings-write", rateLimit: PluginSettingsHost.WritesPerMinute)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var claim = Host.Undo.Begin(arguments.Item(0).StringValue, context.Binding.Package.Id, capture!.LocalAccountKey, PluginSettingsHost.UndoKind);
        if (claim is null) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        try
        {
            var b = new PluginValueBuilder();
            var restored = new List<int>();
            var refused = new List<(string, int)>();
            foreach (var (name, old) in claim.Data.ToList())
            {
                var (setting, refusal) = PluginSettingsTable.Resolve(name);
                if (setting is null || old is null)
                {
                    refused.Add((name, b.String(PluginSettingChecks.Wire(PluginSettingRefusal.Unknown))));
                    continue;
                }

                if (refusal is { } why)
                {
                    refused.Add((name, b.String(PluginSettingChecks.Wire(why))));
                    continue;
                }

                if (!PluginSettingValues.Same(PluginSettingValues.Effective(Host.Values, setting.Row, setting.PrefsKey), old))
                {
                    if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
                    var written = await Host.Values.SetAsync(setting.PrefsKey, old.DeepClone(), ct).ConfigureAwait(false);
                    if (!written.IsOk || !PluginSettingValues.Same(PluginSettingValues.Effective(Host.Values, setting.Row, setting.PrefsKey), old))
                    {
                        refused.Add((name, b.String(PluginSettingChecks.Wire(PluginSettingRefusal.Stopped))));
                        continue;
                    }
                }

                restored.Add(b.String(name));
            }

            if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
            claim.Spend();
            claim = null;
            return Success(b, b.Object(("restored", b.Array([.. restored])), ("refused", b.Object([.. refused]))));
        }
        finally
        {
            claim?.Release();
        }
    }
}
