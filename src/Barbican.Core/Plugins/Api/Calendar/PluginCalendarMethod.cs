// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:308-321 (calendar.* and tasks.* permission table), :2067-2099 (permission gate) and assistant/c.ts:24-26 (calendar:respond,
// calendar.undoDelete needs calendar:write). Barbican adds, per call: the grant is demanded again for the captured login, the live authority and session are rechecked before
// and after every backend await, nothing is projected after a stale check, and every writer call is preceded by MarkMutationIssued.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal abstract class PluginCalendarMethod : IPluginApiMethod
{
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(60);

    protected PluginCalendarMethod(string name, string permission, IPluginCalendarHost host, bool mutates, TimeSpan? deadline = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Permission = permission;
        Descriptor = new(name, [[permission]], deadline ?? (mutates ? WriteDeadline : ReadDeadline), mayMutate: mutates);
    }

    public PluginApiDescriptor Descriptor { get; }

    protected IPluginCalendarHost Host { get; }

    protected string Permission { get; }

    public abstract PluginErrorCode? ValidateArguments(PluginValue arguments);

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        if (ValidateArguments(arguments) is { } invalid) return PluginCallOutcome.Failed(invalid);
        // the active login at call time, never a value the plugin supplied; the grant must name that login for this permission
        var capture = context.CaptureActive();
        if (!capture.IsOk || capture.Value is null) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
        if (!context.Demand(Permission, capture.Value.LocalAccountKey).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        var outcome = await RunAsync(new PluginCalendarCall(context, capture.Value.Session), arguments, ct).ConfigureAwait(false);
        // the account, grant or add-on may have changed while the server answered: drop the answer
        return outcome.IsSuccess && !await context.CheckCurrentAsync(ct).ConfigureAwait(false) ? PluginCallOutcome.Failed(PluginErrorCode.StaleContext) : outcome;
    }

    protected abstract Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct);

    protected static PluginCallOutcome Success(PluginValueBuilder builder, int root) => PluginCallOutcome.Succeeded(builder.Build(root, PluginCalendarProjection.Limits));

    protected static PluginCallOutcome Done()
    {
        var builder = new PluginValueBuilder();
        return Success(builder, builder.Undefined());
    }

    protected static PluginCallOutcome Null()
    {
        var builder = new PluginValueBuilder();
        return Success(builder, builder.Null());
    }

    protected static PluginCallOutcome Fail(PluginErrorCode code) => PluginCallOutcome.Failed(code);
}
