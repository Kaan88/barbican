// Mirrors: Bulwark lib/plugin-sandbox/assistant/d.ts:59-80 (files:read for find, list, getText, getImage; files:write for create, createFolder, move, rename) and host-api.ts:2067-2099 (permission gate).
// Barbican adds, per call: the grant is demanded again for the captured login, the live authority and session are rechecked before and after every backend await, nothing is projected after a stale check, every writer call is
// preceded by MarkMutationIssued and a login without file storage answers Unavailable (d.ts:132-135 needFiles).
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Files;

internal abstract class PluginFilesMethod : IPluginApiMethod
{
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ContentDeadline = TimeSpan.FromSeconds(120);

    protected PluginFilesMethod(string name, string permission, IPluginFilesHost host, bool mutates, bool content = false,
        PluginValueLimits? argumentLimits = null)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Permission = permission;
        Descriptor = new(name, [[permission]], content || mutates ? ContentDeadline : ReadDeadline, mayMutate: mutates)
        {
            ArgumentLimits = argumentLimits,
        };
    }

    public PluginApiDescriptor Descriptor { get; }

    protected IPluginFilesHost Host { get; }

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
        var outcome = await RunAsync(new PluginFilesCall(context, capture.Value.Session), arguments, ct).ConfigureAwait(false);
        // the account, grant or add-on may have changed while the server answered: drop the answer
        return outcome.IsSuccess && !await context.CheckCurrentAsync(ct).ConfigureAwait(false) ? PluginCallOutcome.Failed(PluginErrorCode.StaleContext) : outcome;
    }

    protected abstract Task<PluginCallOutcome> RunAsync(PluginFilesCall call, PluginValue arguments, CancellationToken ct);

    /// <summary>Counts a change to Files against the add-on's budget (d.ts:151-155 countWrite): a refused write counts too.</summary>
    protected bool CountWrite(PluginFilesCall call) => Host.Limiter.TryAcquire("files-write", call.Profile, call.PluginId, PluginFilesData.MaxWritesPerMinute);

    protected static PluginCallOutcome Success(PluginValueBuilder builder, int root) => PluginCallOutcome.Succeeded(builder.Build(root, PluginFileProjection.Limits));

    protected static PluginCallOutcome Fail(PluginErrorCode code) => PluginCallOutcome.Failed(code);
}
