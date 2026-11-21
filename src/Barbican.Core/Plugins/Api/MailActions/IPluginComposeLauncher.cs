// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:79-142 (compose.open fills the composer the way an in-app mailto: link does and the user still presses Send; compose.reply
// asks the mail page to reply to the message open in the reader, "only the open message"). Host seam: the composer service and the reader's open message. Neither call sends mail.
namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>Opens a composer for an add-on. The user always decides whether the message is sent; an implementation never sends.</summary>
public interface IPluginComposeLauncher
{
    /// <summary>Opens a composer filled in with <paramref name="message"/> for the login <paramref name="accountKey"/>.</summary>
    Task<PluginComposeOpenResult> OpenAsync(string accountKey, PluginComposeMessage message, CancellationToken ct);

    /// <summary>Replies to <paramref name="emailId"/> with <paramref name="text"/> as the start of the reply, only when that message is open in the reader.</summary>
    Task<PluginComposeOpenResult> ReplyAsync(string accountKey, string emailId, string text, bool replyAll, CancellationToken ct);
}
