// Mirrors: M2 plan section 4.4 (IListSourceResolver: the __unified folder reads the unified inbox; every other query keeps the M1 path,
// the active account's mail service). Rust mail.rs (UNIFIED folder id). Owner unified.

using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Lists;

namespace Barbican.Presentation.Unified;

/// <summary>Picks the unified source for the unified folder, the active account otherwise.</summary>
public sealed class UnifiedListSourceResolver(UnifiedInbox unified, IActiveMail active) : IListSourceResolver
{
    private readonly ActiveListSourceResolver _default = new(active);

    /// <inheritdoc />
    public IListSource Resolve(ListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.Equals(query.MailboxId, UnifiedRows.FolderId, StringComparison.Ordinal))
        {
            return unified.Source;
        }

        // another folder: the unified window is no longer followed
        unified.Source.Reset();
        return _default.Resolve(query);
    }
}
