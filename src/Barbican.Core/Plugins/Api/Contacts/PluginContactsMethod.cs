// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:178-186 (contacts:read for contact.get / list / search and addressbook.list, contacts:write for contact.create / update / delete and addressbook.create) and :2067-2099
// (permission gate). Barbican adds, per call: the grant is demanded again for the captured login, the live authority and session are rechecked before and after every backend await, nothing is projected after a stale
// check, and every writer call is preceded by MarkMutationIssued.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal abstract class PluginContactsMethod : IPluginApiMethod
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    protected PluginContactsMethod(string name, string permission, IPluginContactsHost host, bool mutates,
        PluginValueLimits? argumentLimits = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Permission = permission;
        Descriptor = new(name, [[permission]], Deadline, mayMutate: mutates)
        {
            ArgumentLimits = argumentLimits,
        };
    }

    public PluginApiDescriptor Descriptor { get; }

    protected IPluginContactsHost Host { get; }

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
        var outcome = await RunAsync(new PluginContactsCall(context, capture.Value.Session), arguments, ct).ConfigureAwait(false);
        // the account, grant or add-on may have changed while the server answered: drop the answer
        return outcome.IsSuccess && !await context.CheckCurrentAsync(ct).ConfigureAwait(false) ? PluginCallOutcome.Failed(PluginErrorCode.StaleContext) : outcome;
    }

    protected abstract Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct);

    protected static PluginCallOutcome Success(PluginValueBuilder builder, int root) => PluginCallOutcome.Succeeded(builder.Build(root, PluginContactProjection.Limits));

    protected static PluginCallOutcome Done()
    {
        var builder = new PluginValueBuilder();
        return Success(builder, builder.Undefined());
    }

    protected static PluginCallOutcome Fail(PluginErrorCode code) => PluginCallOutcome.Failed(code);
}
