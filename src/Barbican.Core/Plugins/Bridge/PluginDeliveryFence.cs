// Host-side continuation of the invocation's live grant and account fences.
// This object is not part of the Bulwark API result/error wire union.
using System.Collections.Immutable;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Bridge;

internal sealed class PluginDeliveryFence(PluginRuntimeBinding binding, PluginGrantSnapshot grant,
    IPluginSessionSource sessions, ImmutableArray<PluginSessionCapture> captures, bool issued)
{
    internal PluginErrorCode Refusal => issued ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext;

    internal bool TryWrite(PluginRuntimeBinding recipient, PluginGrantSnapshot live, Action write) =>
        ReferenceEquals(binding, recipient) && PluginInvocationContext.Matches(live, binding)
        && live.Permissions.SetEquals(grant.Permissions) && live.AccountKeys.SetEquals(grant.AccountKeys)
        && live.ApprovedHttpOrigins.SetEquals(grant.ApprovedHttpOrigins)
        && sessions.TryRunWhileCurrent(captures, () => recipient.TryWriteWhileAlive(write));
}
