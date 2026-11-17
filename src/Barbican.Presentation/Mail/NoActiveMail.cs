// Mirrors: M1-plan §3.3 (replaceable defaults: a service another stream completes is registered with TryAdd). Owner mailcore.

using Barbican.Core.Mail;

namespace Barbican.Presentation.Mail;

/// <summary>No account is signed in (the default until the account manager is registered).</summary>
public sealed class NoActiveMail : IActiveMail
{
    public string? AccountKey => null;

    public string? AccountUser => null;

    public IMailService? Mail => null;

    public IMailSync? Sync => null;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
