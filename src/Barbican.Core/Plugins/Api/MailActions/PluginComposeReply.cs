// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:121-142 doComposeReply (compose:open, host-api.ts:352; emailId and { text?, all? }; text up to 512 KB; the budget of compose.open, 5 a minute shared; replies ONLY to the message open
// in the reader, "a plugin can't start replies to mail the user isn't looking at"; the composer opens with the text as the start of the reply and the user still decides whether to send; resolves true when the composer opened).
// Barbican: the launcher answers Refused when the message is not the one open in the reader; that is Unavailable (Bulwark rejects with "the message is not open in the reader").
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginComposeReply(PluginMailActionHost host) : PluginMailActionMethod("compose.reply", "compose:open", host, true)
{
    private static readonly string[] Allowed = ["text", "all"];

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) && TryOptions(arguments, out _, out _) ? null : PluginErrorCode.InvalidArguments;

    private static bool TryOptions(PluginValue arguments, out string text, out bool all)
    {
        (text, all) = ("", false);
        if (!PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 1), Allowed, false, out var options)) return false;
        if (PluginMailArgs.Prop(options, "text") is { } rawText && !PluginMailArgs.TryString(rawText, PluginComposeInput.MaxBody, out text)) return false;
        if (PluginMailArgs.Prop(options, "all") is { } rawAll)
        {
            if (rawAll.Kind != PluginValueKind.Boolean) return false;
            all = rawAll.BooleanValue;
        }

        return true;
    }

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Compose is not { } compose) return Fail(PluginErrorCode.Unavailable);
        _ = TryOptions(arguments, out var text, out var all);
        if (!Within(context, "compose", PluginComposeOpen.ComposePerMinute)) return Fail(PluginErrorCode.RateLimited);
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var result = await compose.ReplyAsync(capture!.LocalAccountKey, arguments.Item(0).StringValue, text, all, ct).ConfigureAwait(false);
        if (result == PluginComposeOpenResult.Refused) return Fail(PluginErrorCode.Unavailable);
        var b = new PluginValueBuilder();
        return Success(b, b.Boolean(true));
    }
}
