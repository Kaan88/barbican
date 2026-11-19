// Mirrors: Bulwark protocol.ts API result/error union, with fixed safe failure text at the native boundary.
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginCallOutcome
{
    private PluginCallOutcome(bool success, PluginValue? value, PluginErrorCode? error) { IsSuccess = success; Value = value; Error = error; }
    public bool IsSuccess { get; }
    public PluginValue? Value { get; }
    public PluginErrorCode? Error { get; }
    private static readonly PluginValue Undefined = CreateUndefined();
    public static PluginCallOutcome Succeeded(PluginValue? value = null) => new(true, value ?? Undefined, null);
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
