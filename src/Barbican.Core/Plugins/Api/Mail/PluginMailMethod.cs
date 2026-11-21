// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:25-30 sessionClient, host-api.ts:2067-2099 (permission gate, errors that name the method),
// host-api.ts:334-337,156 (email:read for every method here). Barbican adds, per call: the grant is demanded again for the captured login,
// the live authority and session are rechecked after every backend await, and nothing is projected after a stale check.
using Barbican.Core.Accounts;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Mail;

internal abstract class PluginMailMethod : IPluginApiMethod
{
    internal const string Permission = "email:read";
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ScanDeadline = TimeSpan.FromSeconds(120);

    protected PluginMailMethod(string name, IPluginMailReaders readers, bool slow = false)
    {
        Readers = readers ?? throw new ArgumentNullException(nameof(readers));
        Descriptor = new(name, [[Permission]], slow ? ScanDeadline : ReadDeadline);
    }

    public PluginApiDescriptor Descriptor { get; }

    protected IPluginMailReaders Readers { get; }

    public abstract PluginErrorCode? ValidateArguments(PluginValue arguments);

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(arguments);
        if (ValidateArguments(arguments) is { } invalid) return PluginCallOutcome.Failed(invalid);
        // the active login at call time, never a value the plugin supplied; the grant must name that login
        var capture = context.CaptureActive();
        if (!capture.IsOk || capture.Value is null) return PluginCallOutcome.Failed(PluginErrorCode.NoSession);
        if (!context.Demand(Permission, capture.Value.LocalAccountKey).IsOk) return PluginCallOutcome.Failed(PluginErrorCode.PermissionDenied);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return PluginCallOutcome.Failed(PluginErrorCode.StaleContext);
        if (RateKind is { } kind && !Limiter.TryAcquire(kind, context.Binding.Profile, context.Binding.Package.Id, RateLimit)) return PluginCallOutcome.Failed(PluginErrorCode.RateLimited);
        var outcome = await RunAsync(context, capture.Value.Session, arguments, ct).ConfigureAwait(false);
        // the account, grant or add-on may have changed while the server answered: drop the answer
        return outcome.IsSuccess && !await context.CheckCurrentAsync(ct).ConfigureAwait(false) ? PluginCallOutcome.Failed(PluginErrorCode.StaleContext) : outcome;
    }

    /// <summary>The rate-limit bucket, or null when the method is not limited (Bulwark limits only the thread reads).</summary>
    protected virtual string? RateKind => null;

    protected virtual int RateLimit => 60;

    /// <summary>Set by the factory before use.</summary>
    internal IPluginMailRateLimiter Limiter { get; set; } = null!;

    protected abstract Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, IAccountSession session, PluginValue arguments, CancellationToken ct);

    protected static PluginCallOutcome Failure(JmapError? error) => PluginCallOutcome.Failed(error switch
    {
        MethodError { Type: "notFound" } => PluginErrorCode.Unavailable,
        _ => PluginErrorCode.Failed,
    });

    protected static PluginCallOutcome Success(PluginValueBuilder builder, int root) => PluginCallOutcome.Succeeded(builder.Build(root, PluginMailProjection.Limits));
}
