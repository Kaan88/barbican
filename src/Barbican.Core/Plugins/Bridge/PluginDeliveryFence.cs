// Host-side continuation of the invocation's live grant and account fences.
// This object is not part of the Bulwark API result/error wire union.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Bridge;

internal sealed class PluginDeliveryFence(PluginRuntimeBinding binding, PluginGrantSnapshot grant,
    IPluginAuthority authority, IPluginSessionSource sessions, ImmutableArray<PluginSessionCapture> captures, bool issued)
{
    internal PluginErrorCode Refusal => issued ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext;

    internal bool HasCurrentSession(PluginRuntimeBinding recipient) =>
        ReferenceEquals(binding, recipient) && binding.IsAlive && captures.All(sessions.IsCurrent) && binding.IsAlive;

    internal async ValueTask<bool> IsCurrentAsync(PluginRuntimeBinding recipient, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!ReferenceEquals(binding, recipient) || !binding.IsAlive)
        {
            return false;
        }

        var current = await authority.GetCurrentAsync(binding.Package.Id, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return current.IsOk && current.Value is { } live && PluginInvocationContext.Matches(live, binding)
            && live.Permissions.SetEquals(grant.Permissions) && live.AccountKeys.SetEquals(grant.AccountKeys)
            && HasCurrentSession(recipient);
    }
}
