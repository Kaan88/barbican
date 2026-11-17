// Mirrors: M2 plan section 4.5 (IAttachmentExpander; multi-registration; TNEF winmail.dat, G-047). Contract from stage F of M2; owner unified.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Tnef;

/// <summary>Replaces one attachment by the files it contains (winmail.dat to its attachments).</summary>
public interface IAttachmentExpander
{
    bool CanHandle(AttachmentRef attachment);

    Task<Result<IReadOnlyList<AttachmentRef>>> ExpandAsync(MailTarget message, AttachmentRef attachment, CancellationToken ct);
}
