// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2067-2099 (permission gate, errors that name the method), host-api-input.ts:183-196 withinRate (per add-on, per minute) and host-api.ts:277-296 (the permission of each ui method).
// Barbican adds, per call: the active login captured and the grant demanded again for it (the grant of a login the add-on is not running on never counts), the live authority rechecked before and after every await,
// and no value after a stale check. Nothing here changes mail, a task or an event; the only side effects are moving the user's view, asking the views to redraw, and one file the user chooses where to save.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Navigation;

internal abstract class PluginNavigationMethod : IPluginApiMethod
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private readonly bool _needsLogin;

    protected PluginNavigationMethod(string name, IEnumerable<IEnumerable<string>> alternatives, PluginNavigationHost host, bool needsLogin = true, bool mutates = false, TimeSpan? deadline = null,
        PluginValueLimits? argumentLimits = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _needsLogin = needsLogin;
        Descriptor = new(name, alternatives, deadline ?? Deadline, mayMutate: mutates)
        {
            ArgumentLimits = argumentLimits,
        };
    }

    public PluginApiDescriptor Descriptor { get; }

    protected PluginNavigationHost Host { get; }

    /// <summary>The per-minute budget bucket; every method here has one, so an add-on cannot thrash the UI.</summary>
    protected abstract string RateKind { get; }

    protected abstract int RateLimit { get; }

    /// <summary>True when a call over budget is dropped without an error (Bulwark row-badges.ts:34-38 drops extra refreshes).</summary>
    protected virtual bool DropOverBudget => false;

    public abstract PluginErrorCode? ValidateArguments(PluginValue arguments);

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        if (ValidateArguments(arguments) is { } invalid) return PluginCallOutcome.Failed(invalid);
        PluginSessionCapture? capture = null;
        if (_needsLogin)
        {
            // the active login at call time, never a value the add-on supplied; the grant must name that login
            var active = context.CaptureActive();
            if (!active.IsOk || active.Value is null) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
            if (!Holds(context, active.Value.LocalAccountKey, arguments)) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
            capture = active.Value;
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        if (!Host.Limiter.TryAcquire(RateKind, context.Binding.Profile, context.Binding.Package.Id, RateLimit))
        {
            return DropOverBudget ? Done() : PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
        }

        var outcome = await RunAsync(context, capture, arguments, ct).ConfigureAwait(false);
        // the account, grant or add-on may have changed while the app moved: drop the answer
        if (outcome.IsSuccess && !await context.CheckCurrentAsync(ct).ConfigureAwait(false))
        {
            return PluginCallOutcome.Failed(context.HasIssuedMutation ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext);
        }

        return outcome;
    }

    /// <summary>Whether the login holds a permission group the call needs; the default is any group of the descriptor.</summary>
    protected virtual bool Holds(IPluginInvocationContext context, string login, PluginValue arguments) =>
        Descriptor.PermissionAlternatives.Any(group => group.All(permission => context.Demand(permission, login).IsOk));

    protected abstract Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct);

    /// <summary>Rechecks authority right before a write and marks it issued; false means the call went stale and nothing may be written.</summary>
    protected static async ValueTask<bool> BeginWriteAsync(IPluginInvocationContext context, CancellationToken ct)
    {
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return false;
        context.MarkMutationIssued();
        return true;
    }

    protected static PluginCallOutcome Done()
    {
        var builder = new PluginValueBuilder();
        return PluginCallOutcome.Succeeded(builder.Build(builder.Undefined()));
    }

    protected static PluginCallOutcome Answer(bool value)
    {
        var builder = new PluginValueBuilder();
        return PluginCallOutcome.Succeeded(builder.Build(builder.Boolean(value)));
    }

    protected static PluginCallOutcome Fail(PluginErrorCode code) => PluginCallOutcome.Failed(code);

    protected static PluginCallOutcome Stale(IPluginInvocationContext context) =>
        PluginCallOutcome.Failed(context.HasIssuedMutation ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext);
}
