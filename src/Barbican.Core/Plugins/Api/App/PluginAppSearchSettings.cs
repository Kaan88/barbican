// Mirrors: Bulwark lib/plugin-sandbox/host-api-app.ts:63-125 doAppSearchSettings (no static permission, host-api.ts:204: where a setting is, in the app's own words, nothing about the user): the query (at most 200 characters, split into
// words of two or more letters or digits) scores every row of every settings page, 2 for a word in the label, description or section, 1 for a word only in the page's name; best first, ties in page order, one hit per page and label,
// 8 by default and at most 20; resolves to [{ pageId, page, label, description? }].
// Deviations: Barbican's catalog is the index (English only: Barbican has no other language), every page is searched whether or not its server feature exists, and 60 searches a minute per add-on (Bulwark has none).
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Pages;

namespace Barbican.Core.Plugins.Api.App;

internal sealed class PluginAppSearchSettings(PluginSettingsHost host) : PluginAppSettingsMethod("app.searchSettings", null, host, rateKind: "settings-read", rateLimit: PluginSettingsHost.ReadsPerMinute)
{
    internal const int MaxQuery = 200;
    internal const int MaxResults = 20;
    internal const int DefaultResults = 8;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.At(arguments, 0) is { Kind: PluginValueKind.String } query && query.StringValue.Trim().Length > 0 && IsOptions(PluginMailArgs.At(arguments, 1)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var text = arguments.Item(0).StringValue.ToLowerInvariant();
        var words = Words(text.Length > MaxQuery ? text[..MaxQuery] : text);
        var limit = DefaultResults;
        if (PluginMailArgs.Prop(PluginMailArgs.At(arguments, 1), "limit") is { Kind: PluginValueKind.Number } n && double.TryParse(n.NumberText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var asked) && double.IsFinite(asked))
        {
            limit = (int)Math.Min(MaxResults, Math.Max(1, Math.Floor(asked)));
        }

        var b = new PluginValueBuilder();
        if (words.Count == 0) return Success(b, b.Array());
        var scored = new List<(int Score, int Order, string PageId, string Page, string Label, string Description)>();
        var order = 0;
        foreach (var page in SettingsCatalog.Pages())
        {
            var haystack = (page.Label + " " + page.Id.Replace('_', ' ')).ToLowerInvariant();
            foreach (var row in page.Rows.Where(r => r.Kind != SettingRowKind.Section))
            {
                var label = row.Label.Replace("{appName}", "Barbican", StringComparison.Ordinal);
                var description = row.Description.Replace("{appName}", "Barbican", StringComparison.Ordinal);
                var rowText = (label + " " + description + " " + (row.Section ?? "")).ToLowerInvariant();
                var score = 0;
                foreach (var word in words)
                {
                    if (rowText.Contains(word, StringComparison.Ordinal)) score += 2;
                    else if (haystack.Contains(word, StringComparison.Ordinal)) score += 1;
                }

                if (score > 0 && label.Length > 0) scored.Add((score, order++, page.Id, page.Label, label, description));
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<int>();
        foreach (var hit in scored.OrderByDescending(s => s.Score).ThenBy(s => s.Order))
        {
            if (!seen.Add(hit.PageId + "|" + hit.Label)) continue;
            var fields = new List<(string, int)> { ("pageId", b.String(hit.PageId)), ("page", b.String(hit.Page)), ("label", b.String(hit.Label)) };
            if (hit.Description.Length > 0) fields.Add(("description", b.String(hit.Description)));
            rows.Add(b.Object([.. fields]));
            if (rows.Count == limit) break;
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        return Success(b, b.Array([.. rows]));
    }

    private static bool IsOptions(PluginValue? options) => options is null or { Kind: PluginValueKind.Object } or { Kind: PluginValueKind.Null };

    // words of two or more letters or digits
    private static List<string> Words(string text)
    {
        var words = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var part = i < text.Length && char.IsLetterOrDigit(text[i]);
            if (part && start < 0) start = i;
            if (!part && start >= 0)
            {
                if (i - start > 1) words.Add(text[start..i]);
                start = -1;
            }
        }

        return words;
    }
}
