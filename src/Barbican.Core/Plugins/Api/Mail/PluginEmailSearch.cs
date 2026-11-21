// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:127-181 doEmailSearch (query object: text/from/to/subject up to 500 chars, after/before as
// dates sent as ISO UTC, hasAttachment, mailboxId, limit 1-50 default 20, position 0-1,000,000; at least one condition; collapseThreads false)
// and host-api.ts:336,2445. The filter is built here from those named fields only: the plugin never supplies a JMAP filter object (RFC 8621 §4.4.1).
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginEmailSearch(IPluginMailReaders readers) : PluginMailMethod("email.search", readers)
{
    internal const int MaxResults = 50;
    private static readonly string[] TextFields = ["text", "from", "to", "subject"];
    private static readonly string[] DateFields = ["after", "before"];
    private static readonly string[] Allowed = [.. TextFields, .. DateFields, "hasAttachment", "mailboxId", "limit", "position"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryParse(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        _ = TryParse(arguments, out var q);
        return PluginMailListing.RunAsync(Readers, context, session, q!.MailboxId, q.Filter, false, q.Limit, q.Position, ct);
    }

    private sealed record Query(JsonObject Filter, string? MailboxId, int Limit, int Position);

    private static bool TryParse(PluginValue arguments, out Query? query)
    {
        query = null;
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Allowed, true, out var options) || options is null) return false;
        if (!PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "limit"), 1, MaxResults, 20, out var limit)
            || !PluginMailArgs.TryInteger(PluginMailArgs.Prop(options, "position"), 0, 1_000_000, 0, out var position))
        {
            return false;
        }

        var filter = new JsonObject();
        foreach (var key in TextFields)
        {
            if (PluginMailArgs.Prop(options, key) is not { } value) continue;
            if (!PluginMailArgs.TryString(value, 500, out var text)) return false;
            if (text.Trim().Length > 0) filter[key] = text.Trim();
        }

        foreach (var key in DateFields)
        {
            if (PluginMailArgs.Prop(options, key) is not { } value) continue;
            if (!PluginMailArgs.TryString(value, 40, out var text)) return false;
            if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) return false;
            filter[key] = date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        if (PluginMailArgs.Prop(options, "hasAttachment") is { } attachment)
        {
            if (attachment.Kind != PluginValueKind.Boolean) return false;
            filter["hasAttachment"] = attachment.BooleanValue;
        }

        if (filter.Count == 0) return false;
        string? mailbox = null;
        if (PluginMailArgs.Prop(options, "mailboxId") is { } id)
        {
            if (!PluginMailArgs.IsId(id)) return false;
            mailbox = id.StringValue;
        }

        query = new Query(filter, mailbox, limit, position);
        return true;
    }
}
