// Mirrors: M2 plan section 4.4 (the default IListSourceResolver: the active account). Stage F of M2; replaced by unified (wave 2).

using Barbican.Core.Mail.Lists;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Lists;

/// <summary>Resolves every query to the active account's mail service.</summary>
public sealed class ActiveListSourceResolver(IActiveMail active) : IListSourceResolver
{
    /// <inheritdoc />
    public IListSource Resolve(ListQuery query) =>
        new MailServiceListSource(active.Mail ?? throw new InvalidOperationException("No account is active."));
}
