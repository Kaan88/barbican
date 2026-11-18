// Mirrors: Bulwark components/settings/account-settings.tsx (the storage row: "3.5 MB of 1 GB used", a bar), Core/Mail/Attachments/ByteSize.cs for the number format. G-057. Contract from the M6 plan section 4.5; owner set-data.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Account;

/// <summary>Used and total storage of an account.</summary>
public sealed record AccountQuotaUsage(long Used, long? Limit)
{
    /// <summary>Used over limit, clamped to 0..1; -1 without a limit.</summary>
    public double Fraction => Used < 0 ? 0 : throw new PortPendingException("AccountQuotaUsage.Fraction");

    /// <summary>"3.5 MB of 1 GB used", or "3.5 MB used" without a limit.</summary>
    public string Text => Used < 0 ? string.Empty : throw new PortPendingException("AccountQuotaUsage.Text");
}
