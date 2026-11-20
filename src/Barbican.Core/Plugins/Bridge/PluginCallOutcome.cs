// Mirrors: Bulwark protocol.ts API result/error union, with fixed safe failure text at the native boundary.
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginCallOutcome
{
    private readonly PluginDeliveryFence? _delivery;
    private readonly Func<bool>? _networkAllowed;

    private PluginCallOutcome(bool success, PluginValue? value, PluginErrorCode? error, PluginDeliveryFence? delivery = null,
        Func<bool>? networkAllowed = null)
    {
        IsSuccess = success;
        Value = value;
        Error = error;
        _delivery = delivery;
        _networkAllowed = networkAllowed;
    }
    public bool IsSuccess { get; }
    public PluginValue? Value { get; }
    public PluginErrorCode? Error { get; }
    public bool ReconstructFetchBodyText => _networkAllowed is not null;
    private static readonly PluginValue Undefined = CreateUndefined();
    public static PluginCallOutcome Succeeded(PluginValue? value = null) => new(true, value ?? Undefined, null);
    internal PluginCallOutcome WithDeliveryFence(PluginDeliveryFence fence) => new(true, Value, null, fence, _networkAllowed);
    internal static PluginCallOutcome FetchSucceeded(PluginValue value, Func<bool> networkAllowed) =>
        new(true, value, null, networkAllowed: networkAllowed);
    /// <summary>Posts synchronously while the caller holds the live grant and the captured sessions remain current.</summary>
    public bool TryWriteForDelivery(PluginRuntimeBinding recipient, PluginGrantSnapshot live, Action write)
    {
        if (!IsSuccess) return recipient.TryWriteWhileAlive(write);
        var written = false;
        return _delivery?.TryWrite(recipient, live, () =>
        {
            if (_networkAllowed?.Invoke() == false) return;
            write();
            written = true;
        }) == true && written;
    }

    /// <summary>Produces a value-free response when the authority for a queued result is no longer current.</summary>
    public PluginCallOutcome RefuseDelivery() => Failed(_delivery?.Refusal ?? PluginErrorCode.StaleContext);

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
