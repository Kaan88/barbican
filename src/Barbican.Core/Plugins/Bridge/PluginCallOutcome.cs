// Mirrors: Bulwark protocol.ts API result/error union, with fixed safe failure text at the native boundary.
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginCallOutcome
{
    private readonly PluginDeliveryFence? _delivery;

    private PluginCallOutcome(bool success, PluginValue? value, PluginErrorCode? error, PluginDeliveryFence? delivery = null)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
        _delivery = delivery;
    }
    public bool IsSuccess { get; }
    public PluginValue? Value { get; }
    public PluginErrorCode? Error { get; }
    private static readonly PluginValue Undefined = CreateUndefined();
    public static PluginCallOutcome Succeeded(PluginValue? value = null) => new(true, value ?? Undefined, null);
    internal PluginCallOutcome WithDeliveryFence(PluginDeliveryFence fence) => new(true, Value, null, fence);
    /// <summary>Final synchronous session check after serialization and immediately before a native write.</summary>
    public bool HasCurrentDeliverySession(PluginRuntimeBinding recipient) =>
        !IsSuccess || (_delivery?.HasCurrentSession(recipient) ?? false);

    /// <summary>Produces a value-free response when the authority for a queued result is no longer current.</summary>
    public PluginCallOutcome RefuseDelivery() => Failed(_delivery?.Refusal ?? PluginErrorCode.StaleContext);

    /// <summary>
    /// Host-only check for a queued success. Call in the delivery callback, not before queueing it.
    /// This check is not atomic with a later native send; the transport must not queue another hop.
    /// </summary>
    public async ValueTask<PluginCallOutcome> RevalidateForDeliveryAsync(PluginRuntimeBinding recipient, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        ct.ThrowIfCancellationRequested();
        if (!IsSuccess)
        {
            return this;
        }

        return _delivery is { } fence && await fence.IsCurrentAsync(recipient, ct).ConfigureAwait(false)
            ? this : RefuseDelivery();
    }
    private static PluginValue CreateUndefined()
    {
        var builder = new PluginValueBuilder();
        return builder.Build(builder.Undefined());
    }
    public static PluginCallOutcome Failed(PluginErrorCode error)
    {
        if (!Enum.IsDefined(error)) throw new ArgumentOutOfRangeException(nameof(error));
        return new(false, null, error);
    }
    public string? ErrorMessage => Error switch
    {
        null => null,
        PluginErrorCode.UnknownMethod => "Unknown plugin operation.",
        PluginErrorCode.PermissionDenied => "Plugin permission denied.",
        PluginErrorCode.InvalidArguments => "Invalid plugin operation arguments.",
        PluginErrorCode.NoSession => "No authorized account session.",
        PluginErrorCode.StaleContext => "Plugin context changed; retry with current data.",
        PluginErrorCode.Unavailable => "This capability is unavailable.",
        PluginErrorCode.Conflict => "Data changed; reload before retrying.",
        PluginErrorCode.ComposerChanged => "composer_changed",
        PluginErrorCode.AttachmentNotInComposer => "attachment_not_in_composer",
        PluginErrorCode.RateLimited => "Plugin operation rate limit reached.",
        PluginErrorCode.Timeout => "Plugin operation timed out.",
        PluginErrorCode.Cancelled => "Plugin operation cancelled.",
        PluginErrorCode.KeystoreCancelled => "keystore:cancelled",
        PluginErrorCode.KeystoreAskFirst => "keystore:ask-first",
        PluginErrorCode.KeystoreNotFound => "keystore:not-found",
        PluginErrorCode.KeystoreUnsupported => "keystore:unsupported",
        PluginErrorCode.KeystoreLocked => "keystore:locked",
        PluginErrorCode.KeystoreInvalid => "keystore:invalid",
        PluginErrorCode.KeystoreFailed => "keystore:failed",
        PluginErrorCode.UnknownOutcome => "Outcome unknown; reload before retrying.",
        _ => "Plugin operation failed.",
    };
}
