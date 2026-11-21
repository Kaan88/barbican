// Mirrors: Bulwark lib/plugin-sandbox/assistant/b.ts:174-193 doEmailGetThreadCount (the number of messages in the conversation, 1 for a
// message without a thread, its own 120-a-minute budget apart from getThread's, 'no message' when this login may not read it) and b.ts:24.
// Barbican has no summary-only Email/get in IMailService, so the message is read with a one-byte body value limit; the count comes from
// Thread/get via GetThreadAsync (summaries only, no bodies).
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginEmailGetThreadCount(IPluginMailReaders readers) : PluginMailMethod("email.getThreadCount", readers)
{
    internal const int PerMinute = 120;

    protected override string? RateKind => "email.getThreadCount";

    protected override int RateLimit => PerMinute;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginEmailThreadOptions.TryParse(PluginMailArgs.At(arguments, 1), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginEmailThreadOptions.TryParse(PluginMailArgs.At(arguments, 1), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var service = Readers.Reader(session, route.SharedAccountId);
        var found = await service.GetBodyAsync(arguments.Item(0).StringValue, new BodyFetchOptions { MaxBodyValueBytes = 1 }, ct).ConfigureAwait(false);
        if (!found.IsOk) return Failure(found.Error);
        var count = 1;
        if (!string.IsNullOrEmpty(found.Value!.ThreadId))
        {
            var thread = await service.GetThreadAsync(found.Value.ThreadId, ct).ConfigureAwait(false);
            if (!thread.IsOk) return Failure(thread.Error);
            count = Math.Max(1, thread.Value!.Emails.Count);
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, b.Number(count));
    }
}
