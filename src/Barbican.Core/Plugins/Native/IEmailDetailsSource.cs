// Mirrors: M2 plan section 4.5 (IEmailDetailsSource; multi-registration; plugins to the reader-below-subject slot); hook email.details. Contract from stage F of M2; owner unified.

using Barbican.Core.Mail.Model;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>Rows for the area below the subject.</summary>
public interface IEmailDetailsSource
{
    Task<IReadOnlyList<EmailDetailRow>> RowsForAsync(EmailBody email, CancellationToken ct);
}
