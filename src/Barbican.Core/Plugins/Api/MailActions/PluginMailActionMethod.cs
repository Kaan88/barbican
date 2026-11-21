// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2067-2099 (permission gate, errors that name the method), host-api-input.ts:183-196 withinRate (per add-on, per minute) and host-api.ts:334-354 (the permission of each method).
// Barbican adds, per call, what PluginScopedMethod gives every scoped family: the active login captured and demanded again, the live authority rechecked after every await and before a write, and no value
// after a stale check. Nothing here sends mail; email.forward is not offered (see docs/milestones/M6b-api-inventory.md).
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Write;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailActions;

internal abstract class PluginMailActionMethod(string name, string permission, PluginMailActionHost host, bool mutates, bool slow = false)
    : PluginScopedMethod(name, permission, mutates, slow)
{
    /// <summary>Most moves (email.move, email.archive, a moving email.batch) an add-on may start per minute (host-api-mail.ts:251).</summary>
    protected const int MovesPerMinute = 20;

    protected PluginMailActionHost Host { get; } = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>True when the add-on is still inside its per-minute budget for <paramref name="kind"/>; the call is recorded.</summary>
    protected bool Within(IPluginInvocationContext context, string kind, int max) => Host.Limiter.TryAcquire(kind, context.Binding.Profile, context.Binding.Package.Id, max);

    protected static PluginCallOutcome Done()
    {
        var b = new PluginValueBuilder();
        return Success(b, b.Undefined());
    }

    protected static PluginCallOutcome Fail(PluginErrorCode code) => PluginCallOutcome.Failed(code);

    /// <summary>A failed or partly refused write is one plain failure: no server text reaches the add-on.</summary>
    protected static bool Accepted(Result<SetReport> result) => result.IsOk && result.Value!.AllAccepted && result.Value.NotSent.Count == 0;

    protected static PluginErrorCode Code(JmapError? error) => error is MethodError { Type: "notFound" } ? PluginErrorCode.Unavailable : PluginErrorCode.Failed;
}
