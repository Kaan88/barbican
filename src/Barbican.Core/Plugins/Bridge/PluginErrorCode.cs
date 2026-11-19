// Mirrors: Bulwark protocol.ts error responses; native exceptions and paths are not plugin-facing messages.
namespace Barbican.Core.Plugins.Bridge;
public enum PluginErrorCode { UnknownMethod, PermissionDenied, InvalidArguments, NoSession, StaleContext, Unavailable,
    Conflict, RateLimited, Timeout, Cancelled, UnknownOutcome, Failed }
