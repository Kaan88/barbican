// Bulwark components/email/email-viewer.tsx:1035-1050, 1110-1145. Each expanded message owns its body and actions.
using Barbican.Core.Mail.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Mail;

public sealed class ConversationExpansion(string accountKey, string emailId) : ObservableObject
{
    public string AccountKey { get; } = accountKey;
    public string EmailId { get; } = emailId;
    public EmailBody? Body { get; private set; }
    public string Status { get; private set; } = "Loading message…";

    internal void Complete(EmailBody? body)
    {
        Body = body;
        Status = body is null ? "Could not load message. Collapse and reopen to retry." : string.Empty;
        OnPropertyChanged(nameof(Body));
        OnPropertyChanged(nameof(Status));
    }
}
