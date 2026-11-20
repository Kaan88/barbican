// Mirrors: Bulwark lib/plugin-hook-catalog.ts:108-109 (send checks and transforms).
using Barbican.Core.Accounts;

namespace Barbican.Core.Compose;

/// <summary>Creates one add-on authorization scope for an outgoing send attempt.</summary>
public interface IOutgoingAddOnHooks
{
    IOutgoingAddOnSend Start(IAccountSession sendingSession);
}
