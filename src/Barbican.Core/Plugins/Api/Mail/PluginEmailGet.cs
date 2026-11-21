// Mirrors: Bulwark lib/plugin-sandbox/host-api-mail.ts:86-93 doEmailGet (emailId, optional accountId; null when no such message this login may
// read) and host-api.ts:334,2443. Stalwart: Email/get with the reader's property list through the login's session (RFC 8621 §4.2).
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal sealed class PluginEmailGet(IPluginMailReaders readers) : PluginMailMethod("email.get", readers)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 1), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct)
    {
        var id = arguments.Item(0).StringValue;
        _ = PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 1), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        var builder = new PluginValueBuilder();
        if (!route.Allowed) return Success(builder, builder.Null());
        var result = await Readers.Reader(session, route.SharedAccountId).GetBodyAsync(id, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!result.IsOk) return result.Error is Jmap.Errors.MethodError { Type: "notFound" } ? Success(builder, builder.Null()) : Failure(result.Error);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        return Success(builder, PluginMailProjection.ReadView(builder, result.Value!, route.AccountId));
    }
}
