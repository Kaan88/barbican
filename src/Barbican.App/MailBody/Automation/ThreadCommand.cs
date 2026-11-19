#if AUTOMATION
// Mirrors: spec I-222 (a conversation card opens its message in the reader); Rust msgload.rs open :100 from the thread strip (tag rust-final).
// The command shows every member, including older headers not yet revealed in ReaderView.xaml.

using System.Globalization;
using Barbican.Automation;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>thread N|ID</c> command: opens the N-th message (0-based) or the message ID of the reader's conversation strip.</summary>
public sealed class ThreadCommand(ReaderViewModel reader, IConversationOpener opener) : IAutomationCommand
{
    public string Name => "thread";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var a = (args ?? string.Empty).Trim();
        if (a.Length == 0)
        {
            return Task.FromResult(string.Join(' ', reader.Conversation.Select(c => c.IsCurrent ? "*" + c.Id : c.Id)));
        }

        var id = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n < reader.Conversation.Count ? reader.Conversation[n].Id : a;
        return Task.FromResult(opener.OpenConversationMessage(id) ? "ok" : "error not in the conversation " + id);
    }
}
#endif
