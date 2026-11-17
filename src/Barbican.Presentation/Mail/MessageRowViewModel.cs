// Mirrors: Rust main.rs list row (Mail struct in ui/app.slint); spec I-119..I-129. Contract stub from F2; owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail;

/// <summary>One message row (sender, subject, preview, date, flags, thread count).</summary>
public sealed class MessageRowViewModel : ListItemViewModel
{
    public MessageRowViewModel(EmailSummary summary) => Summary = summary;

    public EmailSummary Summary { get; }
}
