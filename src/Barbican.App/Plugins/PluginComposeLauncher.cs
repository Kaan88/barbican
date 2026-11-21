#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:79-142 (compose.open fills the composer the way an in-app mailto: link does and the user still presses Send; compose.reply replies only to the message open in the reader).
// The composer service opens the composer (its own leave guard asks first when a draft is already being written); nothing here sends. Deviations: the composer prefill is plain text, so an add-on's HTML body arrives as its
// text; attachment references are not attached to an opened composer (drafts.create keeps them).
using Barbican.Core.Compose;
using Barbican.Core.Plugins.Api.MailActions;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Mail;

namespace Barbican.App.Plugins;

internal sealed class PluginComposeLauncher(IComposerService composer, ReaderViewModel reader, IDispatcher dispatcher) : IPluginComposeLauncher
{
    public async Task<PluginComposeOpenResult> OpenAsync(string accountKey, PluginComposeMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = new ComposeRequest(accountKey, ComposeKind.New)
        {
            Prefill = new ComposePrefill
            {
                To = string.Join(", ", message.To),
                Cc = string.Join(", ", message.Cc),
                Bcc = string.Join(", ", message.Bcc),
                Subject = message.Subject,
                Body = message.Text,
            },
        };
        return await OpenAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<PluginComposeOpenResult> ReplyAsync(string accountKey, string emailId, string text, bool replyAll, CancellationToken ct)
    {
        string open = "";
        string? list = null;
        await dispatcher.InvokeAsync(() =>
        {
            open = reader.Current.EmailId;
            list = reader.Current.ListAccountKey;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        // only the message the user is looking at, in the login the add-on runs on
        if (!string.Equals(open, emailId, StringComparison.Ordinal) || (list is not null && !string.Equals(list, accountKey, StringComparison.Ordinal))) return PluginComposeOpenResult.Refused;
        return await OpenAsync(new ComposeRequest(accountKey, replyAll ? ComposeKind.ReplyAll : ComposeKind.Reply) { SourceEmailId = emailId, DraftText = text.Length == 0 ? null : text }, ct).ConfigureAwait(false);
    }

    private async Task<PluginComposeOpenResult> OpenAsync(ComposeRequest request, CancellationToken ct)
    {
        var opened = false;
        await dispatcher.InvokeAsync(async () => opened = await composer.OpenAsync(request, ct).ConfigureAwait(true) is not null).ConfigureAwait(false);
        return opened ? PluginComposeOpenResult.Opened : PluginComposeOpenResult.Refused;
    }
}
#endif
