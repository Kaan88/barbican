// Mirrors: Rust jmap.rs:1293 method_error; RFC 8620 §3.6.2 (method-level errors), §5.2, §5.5, §5.6 (cannotCalculateChanges,
// anchorNotFound, unsupportedSort/Filter, tooManyChanges); Bulwark lib/jmap/client.ts ~218 UNREPORTED_METHOD_ERRORS. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Errors;

/// <summary>Method-level errors (<c>["error", {type, description}, callId]</c>) in words.</summary>
public static class MethodErrors
{
    /// <summary>The user message for a method error type (with the server's description where it helps).</summary>
    public static string Message(string type, string? description)
    {
        ArgumentNullException.ThrowIfNull(type);
        var d = description ?? "";
        var withDetail = d.Length == 0 ? "" : $": {d}";
        return type switch
        {
            "unknownMethod" or "unknownCapability" => $"The server does not support this ({type}).",
            "forbidden" or "accountReadOnly" => "You do not have permission to do that.",
            "accountNotFound" or "accountNotSupportedByMethod" => "This account does not support that.",
            "requestTooLarge" => "Too many items in one request.",
            "cannotCalculateChanges" => "The list changed too much; reload.",
            "tooManyChanges" => "Too many changes at once; reload.",
            "stateMismatch" => "Someone else changed this at the same time; try again.",
            "anchorNotFound" => "The list changed; reload.",
            "serverUnavailable" or "serverFail" or "serverPartialFail" => $"The server had a problem ({type}){withDetail}; try again.",
            "unsupportedSort" or "unsupportedFilter" => $"The server can't sort or search like that{withDetail}.",
            _ => d.Length == 0 ? $"Server error: {type}" : $"{type}: {d}",
        };
    }

    /// <summary>The typed error of an <c>error</c> response to a call of <paramref name="method"/>.</summary>
    public static MethodError From(string method, JsonNode? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var type = args.Str("type") ?? "error";
        var description = args.Str("description");
        return new MethodError(method, type, description) { Message = Message(type, description) };
    }
}
