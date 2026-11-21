// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1496-1535 doEmailUpdateKeywords (email:write, :237; emailId, an object of keyword: true | false with at most 20 entries, optional accountId; every keyword checked as for
// email.setKeyword; set and cleared in one Email/set so a swap never leaves both or neither; an empty object changes nothing and answers true). Resolves true when the change went through.
// Deviation: Bulwark patches a message the list shows through the list so the row changes at once; Barbican changes it on the server and the sync brings the row.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailUpdateKeywords(PluginMailActionHost host) : PluginMailActionMethod("email.updateKeywords", "email:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && TryChanges(PluginMailArgs.At(arguments, 1), out _) && PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 2), out _)
            ? null : PluginErrorCode.InvalidArguments;

    private static bool TryChanges(PluginValue? value, out Dictionary<string, bool> changes)
    {
        changes = new(StringComparer.Ordinal);
        if (value is not { Kind: PluginValueKind.Object } || value.PropertyNames.Count > PluginMailActionArgs.MaxKeywordChanges) return false;
        foreach (var name in value.PropertyNames)
        {
            var flag = value.Property(name);
            if (!PluginMailActionArgs.IsPluginKeyword(name, true) || flag.Kind != PluginValueKind.Boolean) return false;
            changes[name] = flag.BooleanValue;
        }

        return true;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = TryChanges(PluginMailArgs.At(arguments, 1), out var changes);
        var b = new PluginValueBuilder();
        if (changes.Count == 0) return Success(b, b.Boolean(true));
        var session = capture!.Session;
        _ = PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 2), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var set = await Host.Writers.ForSession(session, route.SharedAccountId).UpdateKeywordsAsync([arguments.Item(0).StringValue], changes, ct).ConfigureAwait(false);
        return Accepted(set) ? Success(b, b.Boolean(true)) : Fail(PluginErrorCode.Failed);
    }
}
