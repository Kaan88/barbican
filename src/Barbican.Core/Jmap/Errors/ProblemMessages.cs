// Mirrors: Rust jmap.rs:112 problem_message; RFC 7807 problem details as RFC 8620 §3.6.1 uses them (urn:ietf:params:jmap:error:
// unknownCapability, notJSON, notRequest, limit with `limit`); Stalwart crates/jmap/src/api/request.rs. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Errors;

/// <summary>The text of a request-level problem-details body.</summary>
public static class ProblemMessages
{
    /// <summary>A message naming the limit or capability the server complained about, or the HTTP status.</summary>
    public static string Message(int status, JsonObject problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var type = problem.Str("type") ?? "";
        var detail = problem.Str("detail") ?? "";
        var kind = type[(type.LastIndexOf(':') + 1)..];
        var withDetail = detail.Length == 0 ? "" : $": {detail}";
        return kind switch
        {
            "limit" => $"The request is over the server's {problem.Str("limit") ?? "size"} limit.",
            "unknownCapability" => $"The server does not support a capability this needs{(detail.Length == 0 ? "" : $" ({detail})")}.",
            "notJSON" or "notRequest" => $"The server could not read the request ({status}).",
            _ => $"Request failed ({status}){withDetail}",
        };
    }

    /// <summary>The typed error of a problem-details body; its message is <see cref="Message"/>.</summary>
    public static ProblemError ToError(int status, JsonObject problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new ProblemError(status, problem.Str("type") ?? "", problem.Str("detail"), problem.Str("limit"))
        {
            Message = Message(status, problem),
        };
    }
}
