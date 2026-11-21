// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:143-167 doDraftsCreate (email:write, host-api.ts:354; the message object of compose.open; saved in the Drafts folder from the account's identity, without opening a composer;
// at most 10 drafts a minute per add-on; resolves the draft's id) and lib/jmap/client.ts createDraft; Stalwart crates/jmap/src/email/set.rs (create with $draft). NOTHING IS SENT: the draft stays in Drafts for the user.
// The save goes through the same draft service as the composer's own save (DraftService: identity chosen from the account's own identities, $draft keyword, Drafts folder, onBeforeDraftAutoSave and onDraftAutoSave hooks).
// Deviation: Bulwark uses the user's preferred primary identity; Barbican's draft service takes the account's first identity when none is named (no add-on may name one).
using Barbican.Core.Compose;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal sealed class PluginDraftsCreate(PluginMailActionHost host) : PluginMailActionMethod("drafts.create", "email:write", host, true)
{
    private const int DraftsPerMinute = 10;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginComposeInput.TryRead(PluginMailArgs.At(arguments, 0), out _) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        if (Host.Drafts is not { } drafts) return Fail(PluginErrorCode.Unavailable);
        _ = PluginComposeInput.TryRead(PluginMailArgs.At(arguments, 0), out var message);
        if (!Within(context, "drafts", DraftsPerMinute)) return Fail(PluginErrorCode.RateLimited);
        var draft = new ComposeDraft
        {
            AccountId = capture!.LocalAccountKey,
            Kind = ComposeKind.New,
            IdentityId = "",
            To = string.Join(", ", message.To),
            Cc = string.Join(", ", message.Cc),
            Bcc = string.Join(", ", message.Bcc),
            Subject = message.Subject,
            Body = message.Html is { } html ? new Doc(DocHtml.Import(html)) : Doc.FromText(message.Text),
            PlainText = message.Html is null,
            Attachments = [.. message.Attachments.Select(a => new DraftAttachment { Name = a.Name, Type = a.Type, Size = a.Size, BlobId = a.BlobId })],
        };
        if (!await BeginWriteAsync(context, ct).ConfigureAwait(false)) return Stale(context);
        var saved = await drafts.SaveAsync(draft, ct).ConfigureAwait(false);
        if (!saved.IsOk || string.IsNullOrEmpty(saved.Value!.DraftId)) return Fail(PluginErrorCode.Failed);
        var b = new PluginValueBuilder();
        return Success(b, b.String(saved.Value.DraftId));
    }
}
