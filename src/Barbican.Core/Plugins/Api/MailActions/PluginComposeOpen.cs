// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:64-118 doComposeOpen (compose:open, host-api.ts:350; the message object; at most 5 compose.open or compose.reply calls a minute per add-on; opens the composer filled in the way an
// in-app mailto: link does and the USER STILL PRESSES SEND; asks first when a draft is already open; resolves "opened", or "navigating" when the mail view has to open first) and :143 (no call sends mail).
// Barbican: the composer service behind IPluginComposeLauncher opens the composer; nothing is sent from here. If the user keeps the draft being written, nothing opens and the add-on gets Failed.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginComposeOpen(PluginMailActionHost host) : PluginMailActionMethod("compose.open", "compose:open", host, true)
{
    internal const int ComposePerMinute = 5;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginComposeInput.TryRead(PluginMailArgs.At(arguments, 0), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Compose is not { } compose) return Fail(PluginErrorCode.Unavailable);
        _ = PluginComposeInput.TryRead(PluginMailArgs.At(arguments, 0), out var message);
        if (!Within(context, "compose", ComposePerMinute)) return Fail(PluginErrorCode.RateLimited);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var result = await compose.OpenAsync(capture!.LocalAccountKey, message, ct).ConfigureAwait(false);
        if (result == PluginComposeOpenResult.Refused) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, b.String(result == PluginComposeOpenResult.Opened ? "opened" : "navigating"));
    }
}
