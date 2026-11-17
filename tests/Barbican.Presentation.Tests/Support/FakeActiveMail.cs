// Test double for IActiveMail. Owner mailcore.

using Barbican.Core.Mail;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Tests.Support;

internal sealed class FakeActiveMail : IActiveMail
{
    public string? AccountKey { get; set; } = "acct";

    public string? AccountUser { get; set; } = "user";

    public IMailService? Mail { get; set; }

    public IMailSync? Sync { get; set; }

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
