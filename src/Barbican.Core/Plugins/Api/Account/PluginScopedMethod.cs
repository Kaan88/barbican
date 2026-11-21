// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2067-2099 (permission gate; the method's own validation), host-api-input.ts:25-30 sessionClient
// (the signed-in login, never one the add-on names). Shared base of the account, identity, tag-definition and template methods. Barbican adds, per
// call: the active login is captured at call time, the grant is demanded again for that login, the live authority and session are rechecked after
// every backend await, nothing is projected after a stale check, and a write checks the authority immediately before it is issued.
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Account;

internal abstract class PluginScopedMethod : IPluginApiMethod
{
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SlowDeadline = TimeSpan.FromSeconds(120);
    private readonly string? _permission;
    private readonly IPluginMailRateLimiter? _limiter;

    // permission: the one permission Bulwark requires, or null for a method with no static gate (host-api.ts:200-202).
    protected PluginScopedMethod(string name, string? permission, bool mayMutate = false, bool slow = false, IPluginMailRateLimiter? limiter = null,
        PluginValueLimits? argumentLimits = null)
    {
        _permission = permission;
        _limiter = limiter;
        Descriptor = new(name, permission is null ? [] : [[permission]], slow ? SlowDeadline : ReadDeadline, mayMutate: mayMutate)
        {
            ArgumentLimits = argumentLimits,
        };
    }

    public PluginApiDescriptor Descriptor { get; }

    /// <summary>The per-minute budget bucket, or null when the method is not limited.</summary>
    protected virtual string? RateKind => null;

    protected virtual int RateLimit => 60;

    public abstract PluginErrorCode? ValidateArguments(PluginValue arguments);

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        if (ValidateArguments(arguments) is { } invalid) return PluginCallOutcome.Failed(invalid);
        PluginSessionCapture? capture = null;
        if (_permission is not null)
        {
            // the active login at call time, never a value the add-on supplied; the grant must name that login
            var active = context.CaptureActive();
            if (!active.IsOk || active.Value is null) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
            if (!context.Demand(_permission, active.Value.LocalAccountKey).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
            capture = active.Value;
        }

        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        if (RateKind is { } kind && _limiter is not null && !_limiter.TryAcquire(kind, context.Binding.Profile, context.Binding.Package.Id, RateLimit))
        {
            return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
        }

        var outcome = await RunAsync(context, capture, arguments, ct).ConfigureAwait(false);
        // the account, grant or add-on may have changed while the answer was prepared: drop it
        if (outcome.IsSuccess && !await context.CheckCurrentAsync(ct).ConfigureAwait(false))
        {
            return PluginCallOutcome.Failed(context.HasIssuedMutation ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext);
        }

        return outcome;
    }

    /// <summary><paramref name="capture"/> is null only for a method without a permission.</summary>
    protected abstract Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct);

    /// <summary>Rechecks authority right before a write and marks it issued; false means the call went stale and nothing may be written.</summary>
    protected static async Task<bool> BeginWriteAsync(IPluginInvocationContext context, CancellationToken ct)
    {
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return false;
        context.MarkMutationIssued();
        return true;
    }

    protected static PluginCallOutcome Stale(IPluginInvocationContext context) =>
        PluginCallOutcome.Failed(context.HasIssuedMutation ? PluginErrorCode.UnknownOutcome : PluginErrorCode.StaleContext);

    protected static PluginCallOutcome Success(PluginValueBuilder builder, int root) => PluginCallOutcome.Succeeded(builder.Build(root, PluginMailProjection.Limits));
}
