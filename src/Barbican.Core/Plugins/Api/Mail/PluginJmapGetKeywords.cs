// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1840-1854 (keywordDiscoveryOptions: optional options object, limit an integer 1-25000; other
// properties are ignored there as here) and doJmapGetKeywords, host-api.ts:156,2170 (email:read, no privileged tier), and
// lib/jmap/client.ts:3334-3434 discoverKeywords / getKeywords (the message walk: Email/query newest first, pages of at most 500, counting each
// set keyword, server-set attachment keywords dropped, labels named without the "$label:" prefix). Deviation: Bulwark first tries its own
// "https://bulwarkmail.com/ns/jmap/keywords" Keyword/get extension; Stalwart has none, so only the message walk exists here. Barbican walks
// through IMailService.QueryAsync pages of the login's own account; there is no raw JMAP forwarding. Result: {keywords, labels, scanned, total, complete}.
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginJmapGetKeywords(IPluginMailReaders readers) : PluginMailMethod("jmap.getKeywords", readers, slow: true)
{
    internal const int MaxScan = 25_000;
    private const int PageSize = 500;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryLimit(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        _ = TryLimit(arguments, out var cap);
        var service = Readers.Reader(session, null);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        int scanned = 0, total = 0;
        var complete = false;
        while (scanned < cap)
        {
            var want = Math.Min(PageSize, cap - scanned);
            var page = await service.QueryAsync(new ListQuery { PageSize = want }, scanned > 0 ? new ListCursor(string.Empty) { Position = scanned } : null, ct).ConfigureAwait(false);
            if (!page.IsOk) break;
            if (scanned == 0) total = page.Value!.Total ?? 0;
            foreach (var row in page.Value!.Items)
            {
                foreach (var keyword in PluginMailProjection.TrueKeys(row.Keywords.Map).Where(k => !PluginMailProjection.IsServerSet(k)))
                {
                    counts[keyword] = counts.GetValueOrDefault(keyword) + 1;
                }
            }

            var returned = page.Value.Items.Count + page.Value.DroppedIds.Count;
            scanned += returned;
            if (returned == 0 || returned < want) { complete = true; break; }
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        var keywords = b.Object([.. counts.Select(x => (x.Key, b.Number(x.Value)))]);
        var labels = b.Array([.. counts.Select(x => b.Object(
            ("id", b.String(x.Key)), ("name", b.String(x.Key.StartsWith("$label:", StringComparison.Ordinal) ? x.Key["$label:".Length..] : x.Key)),
            ("color", b.Null()), ("total", b.Number(x.Value)), ("unread", b.Number(0)), ("isProviderLabel", b.Boolean(false)), ("source", b.String("message"))))]);
        return Success(b, b.Object(("keywords", keywords), ("labels", labels), ("scanned", b.Number(scanned)),
            ("total", b.Number(Math.Max(total, scanned))), ("complete", b.Boolean(complete))));
    }

    private static bool TryLimit(PluginValue arguments, out int limit)
    {
        limit = MaxScan;
        var options = PluginMailArgs.At(arguments, 0);
        if (options is null) return true;
        return options.Kind == PluginValueKind.Object && PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "limit"), 1, MaxScan, MaxScan, out limit);
    }
}
