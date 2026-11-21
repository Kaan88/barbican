// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2352-2365 email.setKeyword / email.removeKeyword (email:write, :235-236; emailId, keyword, optional accountId; resolves undefined), :1421-1446 assertPluginKeyword / assertMailKeyword
// (RFC 5788 charset up to 128 characters, not a keyword the server sets, not $draft, $junk, $notjunk or $phishing: those are left to the app's own actions). Stalwart crates/jmap/src/email/set.rs (keywords/<name> patch).
// A message of a shared account is changed only through the login that shows it (the same routing as email.get); an account the login does not show reaches no server and is Unavailable.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginEmailSetKeyword(PluginMailActionHost host, bool on) : PluginMailActionMethod(on ? "email.setKeyword" : "email.removeKeyword", "email:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && PluginMailActionArgs.TryKeyword(PluginMailArgs.At(arguments, 1), true, out _) && PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 2), out _)
            ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var session = capture!.Session;
        _ = PluginMailActionArgs.TryKeyword(PluginMailArgs.At(arguments, 1), true, out var keyword);
        _ = PluginMailArgs.TryAccount(PluginMailArgs.At(arguments, 2), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var patch = new Dictionary<string, bool>(StringComparer.Ordinal) { [keyword] = on };
        var set = await Host.Writers.ForSession(session, route.SharedAccountId).UpdateKeywordsAsync([arguments.Item(0).StringValue], patch, ct).ConfigureAwait(false);
        return Accepted(set) ? Done() : Fail(PluginErrorCode.Failed);
    }
}
