// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.settings.ts:165-208 doDescribeSettings (settings:read, c.ts:33): the settings named, in the user's words: label, page, current value and, for a change, the new one, and whether it may be made now.
// The argument is an object of changes (name to proposed value) or a list of names, 1 to 20 of them; a name longer than 100 characters is skipped. Resolves to { [name]: { key, label, page, pageLabel, current, currentLabel,
// next?, nextLabel?, allowed, reason?, options? } }; a name Bulwark or Barbican does not allow answers allowed false with a reason and no label.
// Deviations: the labels are the catalog's (English); next is echoed only when it is a value the setting takes; a list that holds something other than a string is InvalidArguments (Bulwark turns it into text); 60 calls a minute per add-on.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Plugins.Api.App;

internal sealed class PluginAppDescribeSettings(PluginSettingsHost host) : PluginAppSettingsMethod("app.describeSettings", "settings:read", host, rateKind: "settings-read", rateLimit: PluginSettingsHost.ReadsPerMinute)
{
    private const int MaxKeyLength = 100;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryRead(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (!TryRead(arguments, out var asked)) return PluginCallOutcome.Failed(PluginErrorCode.InvalidArguments);
        var b = new PluginValueBuilder();
        var fields = new List<(string, int)>();
        foreach (var (name, next) in asked)
        {
            if (name.Length > MaxKeyLength) continue;
            var refusal = PluginSettingChecks.Check(name, next, out var setting, out var canonical);
            fields.Add((name, setting is null ? Unknown(b, name, refusal ?? PluginSettingRefusal.Unknown) : Describe(b, name, setting, refusal, next, canonical)));
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        return Success(b, b.Object([.. fields]));
    }

    private int Describe(PluginValueBuilder b, string name, PluginResolvedSetting setting, PluginSettingRefusal? refusal, PluginValue? next, System.Text.Json.Nodes.JsonNode? canonical)
    {
        var current = PluginSettingValues.Effective(Host.Values, setting.Row, setting.PrefsKey);
        var page = SettingsCatalog.Pages().FirstOrDefault(p => string.Equals(p.Id, setting.Row.Page, StringComparison.Ordinal));
        var fields = new List<(string, int)>
        {
            ("key", b.String(name)), ("label", b.String(setting.Row.Label)), ("page", b.String(setting.Row.Page)), ("pageLabel", b.String(page?.Label ?? setting.Row.Page)),
            ("current", PluginSettingValues.Emit(b, current) ?? b.Null()), ("currentLabel", b.String(PluginSettingValues.Label(setting, current))),
        };
        if (next is not null && PluginSettingValues.TryRead(next, out var proposed))
        {
            fields.Add(("next", PluginSettingValues.Emit(b, proposed) ?? b.Null()));
            fields.Add(("nextLabel", b.String(PluginSettingValues.Label(setting, canonical ?? proposed))));
        }

        fields.Add(("allowed", b.Boolean(refusal is null)));
        if (refusal is { } why) fields.Add(("reason", b.String(PluginSettingChecks.Wire(why))));
        var options = Options(b, setting, refusal);
        if (options.Count > 0) fields.Add(("options", b.Array([.. options])));
        return b.Object([.. fields]);
    }

    // a short list of the values the setting may take now: on and off for a switch (only the direction a safety net allows), the row's values for a choice
    private static List<int> Options(PluginValueBuilder b, PluginResolvedSetting setting, PluginSettingRefusal? refusal)
    {
        var rows = new List<int>();
        if (refusal is not null) return rows;
        IEnumerable<(System.Text.Json.Nodes.JsonNode Value, string Label)> candidates = setting.Rule.Kind switch
        {
            PluginSettingKind.Boolean => [(System.Text.Json.Nodes.JsonValue.Create(true)!, ""), (System.Text.Json.Nodes.JsonValue.Create(false)!, "")],
            PluginSettingKind.Choice => PluginSettingValues.Choices(setting),
            _ => [],
        };
        foreach (var (value, _) in candidates)
        {
            if (!PluginSettingValues.Allows(setting, value, out _) || PluginSettingValues.Emit(b, value) is not { } id) continue;
            rows.Add(b.Object(("value", id), ("label", b.String(PluginSettingValues.Label(setting, value)))));
        }

        return rows;
    }

    private static int Unknown(PluginValueBuilder b, string name, PluginSettingRefusal reason) => b.Object(
        ("key", b.String(name)), ("label", b.String(name)), ("page", b.String("")), ("pageLabel", b.String("")), ("current", b.Null()), ("currentLabel", b.String("")),
        ("allowed", b.Boolean(false)), ("reason", b.String(PluginSettingChecks.Wire(reason))));

    // an object of changes or a list of names, 1 to 20; keys are unique (a list's repeats count once, as Bulwark's fromEntries does)
    private static bool TryRead(PluginValue arguments, out List<(string Name, PluginValue? Next)> asked)
    {
        asked = [];
        var arg = PluginMailArgs.At(arguments, 0);
        if (arg is { Kind: PluginValueKind.Array })
        {
            for (var i = 0; i < arg.Count && i <= PluginSettingsTable.MaxPerCall; i++)
            {
                if (arg.Item(i) is not { Kind: PluginValueKind.String } name) return false;
                if (!asked.Any(a => string.Equals(a.Name, name.StringValue, StringComparison.Ordinal))) asked.Add((name.StringValue, null));
            }
        }
        else if (arg is { Kind: PluginValueKind.Object })
        {
            foreach (var key in arg.PropertyNames) asked.Add((key, arg.Property(key)));
        }
        else
        {
            return false;
        }

        return asked.Count is > 0 and <= PluginSettingsTable.MaxPerCall;
    }
}
