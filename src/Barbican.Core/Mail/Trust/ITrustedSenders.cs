// Mirrors: M2 plan section 4.5 (ITrustedSenders); Bulwark components/trusted-senders-modal.tsx, lib/sender-icon-access.ts; I-209 (trust per sender). Contract from stage F of M2; owner reader.

using Barbican.Core.Common;

namespace Barbican.Core.Mail.Trust;

/// <summary>Senders whose remote images and links the user allowed.</summary>
public interface ITrustedSenders
{
    bool IsTrusted(string address);

    Task<Result<Unit>> AddAsync(string address, CancellationToken ct);

    Task<Result<Unit>> RemoveAsync(string address, CancellationToken ct);

    IReadOnlyList<string> All { get; }
}
