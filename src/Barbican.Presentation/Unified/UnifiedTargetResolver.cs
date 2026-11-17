// Mirrors: M2 plan section 4.4 (IMailTargetResolver: a unified row names its login and shared account); Rust unified.rs:43 parse_row.
// Owner unified. A unified row keeps its row id as the email id: the writer (UnifiedMailWriter) sends it to its own account.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Lists;

namespace Barbican.Presentation.Unified;

/// <summary>The message a list row stands for, with the account it lives in.</summary>
public sealed class UnifiedTargetResolver(IActiveMail active) : IMailTargetResolver
{
    /// <inheritdoc />
    public MailTarget? Resolve(MessageRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var key = active.AccountKey ?? string.Empty;
        var shared = UnifiedRows.ParseRow(row.Id)?.SharedAccount ?? string.Empty;
        return new MailTarget(key, shared, row.Id, row.Summary.ThreadId ?? row.Id, row.Key, row.Summary.MailboxIds, row.Summary.Keywords);
    }
}
