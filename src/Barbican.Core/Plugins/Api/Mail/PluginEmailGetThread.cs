// Mirrors: Bulwark lib/plugin-sandbox/assistant/b.ts:147-172 doEmailGetThread (emailId, options {accountId}; the conversation of the message,
// oldest first, at most the newest MAX_THREAD_MESSAGES = 50, as read views; the message alone when the server lists no thread; 60 reads a
// minute per plugin; 'no message' when this login may not read it) and b.ts:23 (email:read). Barbican: GetBodyAsync for the message,
// GetThreadAsync (Thread/get + Email/get, RFC 8621 §3) for the members, GetBodyAsync for the bodies of the newest fifty.
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginEmailGetThread(IPluginMailReaders readers) : PluginMailMethod("email.getThread", readers, slow: true)
{
    internal const int MaxMessages = 50;
    internal const int PerMinute = 60;

    protected override string? RateKind => "email.getThread";

    protected override int RateLimit => PerMinute;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginEmailThreadOptions.TryParse(PluginMailArgs.At(arguments, 1), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        _ = PluginEmailThreadOptions.TryParse(PluginMailArgs.At(arguments, 1), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return PluginCallOutcome.Failed(PluginErrorCode.Unavailable);
        var service = Readers.Reader(session, route.SharedAccountId);
        var found = await service.GetBodyAsync(arguments.Item(0).StringValue, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!found.IsOk) return Failure(found.Error);
        var list = new List<EmailBody> { found.Value! };
        if (!string.IsNullOrEmpty(found.Value!.ThreadId))
        {
            var thread = await service.GetThreadAsync(found.Value.ThreadId, ct).ConfigureAwait(false);
            if (!thread.IsOk) return Failure(thread.Error);
            var newest = thread.Value!.Emails.OrderBy(e => e.ReceivedAt).TakeLast(MaxMessages).ToList();
            if (newest.Count > 0)
            {
                list.Clear();
                foreach (var row in newest)
                {
                    if (string.Equals(row.Id, found.Value.Id, StringComparison.Ordinal)) { list.Add(found.Value); continue; }
                    var body = await service.GetBodyAsync(row.Id, new BodyFetchOptions(), ct).ConfigureAwait(false);
                    if (body.IsOk) list.Add(body.Value!);
                    else if (body.Error is not Jmap.Errors.MethodError { Type: "notFound" }) return Failure(body.Error);
                }
            }
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        var views = list.OrderBy(e => e.ReceivedAt).TakeLast(MaxMessages).Select(e => PluginMailProjection.ReadView(b, e, route.AccountId)).ToArray();
        return Success(b, b.Array(views));
    }
}
