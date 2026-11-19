// Mirrors: Bulwark protocol.ts API result/error union, with fixed safe failure text at the native boundary.
using System.Text.Json;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginCallOutcome
{
    private PluginCallOutcome(bool success, JsonElement? value, PluginErrorCode? error) { IsSuccess = success; Value = value; Error = error; }
    public bool IsSuccess { get; }
    public JsonElement? Value { get; }
    public PluginErrorCode? Error { get; }
    public static PluginCallOutcome Succeeded(JsonElement value = default) => new(true, value.ValueKind == JsonValueKind.Undefined ? null : value.Clone(), null);
    public static PluginCallOutcome Failed(PluginErrorCode error) => new(false, null, error);
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
        PluginErrorCode.UnknownOutcome => "Outcome unknown; reload before retrying.",
        _ => "Plugin operation failed.",
    };
}
