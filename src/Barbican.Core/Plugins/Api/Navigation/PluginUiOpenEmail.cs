// Mirrors: Bulwark lib/plugin-sandbox/open-email.ts:129-158 requestOpenEmail (the message object {id, accountId?}, unknown keys refused; a message is reached through the login that reads its account; a message no login reads, or one
// its account does not have, is an error; at most 30 opens a minute per add-on, IN_PLACE_OPENS_PER_MINUTE :121) and host-api.ts:294 (email:read: opening a message shows its content, as reading it does).
// Barbican reads the message once through the captured login (so a foreign account, or an id the login cannot read, is Unavailable and the reader is never asked) and hands the app only the summary of a message it has just read.
// Deviations: the app's reader opens messages of the login on screen only, so a shared account's message is refused by the navigator (Unavailable); Bulwark's second budget (5 permalink loads a minute) has no equivalent.
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal sealed class PluginUiOpenEmail(PluginNavigationHost host) : PluginNavigationMethod("ui.openEmail", [["email:read"]], host)
{
    private static readonly string[] Keys = ["id", "accountId"];

    protected override string RateKind => "ui-open-email";

    protected override int RateLimit => 30;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.TryOptions(PluginMailArgs.At(arguments, 0), Keys, true, out var email) && PluginMailArgs.IsId(PluginMailArgs.Prop(email, "id"))
            && PluginMailArgs.TryAccount(PluginMailArgs.Prop(email, "accountId"), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Navigator is not { } navigator) return Fail(PluginErrorCode.Unavailable);
        var session = capture!.Session;
        var email = PluginMailArgs.At(arguments, 0)!;
        var id = email.Property("id").StringValue;
        _ = PluginMailArgs.TryAccount(PluginMailArgs.Prop(email, "accountId"), out var requested);
        var route = PluginMailRouting.Resolve(session, requested);
        if (!route.Allowed) return Fail(PluginErrorCode.Unavailable);
        var read = await Host.Readers.Reader(session, route.SharedAccountId).GetBodyAsync(id, new BodyFetchOptions(), ct).ConfigureAwait(false);
        if (!read.IsOk) return Fail(read.Error is Jmap.Errors.MethodError { Type: "notFound" } ? PluginErrorCode.Unavailable : PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var outcome = await navigator.OpenEmailAsync(capture.LocalAccountKey, route.SharedAccountId, Summary(read.Value!), ct).ConfigureAwait(false);
        return outcome == PluginOpenEmailOutcome.Opened ? Done() : Fail(PluginErrorCode.Unavailable);
    }

    private static EmailSummary Summary(EmailBody body) => new()
    {
        Id = body.Id,
        ThreadId = body.ThreadId,
        MailboxIds = body.MailboxIds,
        Keywords = body.Keywords,
        Size = body.Size,
        ReceivedAt = body.ReceivedAt,
        SentAt = body.SentAt,
        From = body.From,
        To = body.To,
        Cc = body.Cc,
        Subject = body.Subject,
        Preview = body.Preview,
        HasAttachment = body.HasAttachment,
        Attachments = body.Attachments,
    };
}
