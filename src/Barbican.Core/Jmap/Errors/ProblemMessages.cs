// Mirrors: Rust jmap.rs:112 problem_message; RFC 7807, RFC 8620 §3.6.1. Stub from F2; owner jmap.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Errors;

/// <summary>The text of a request-level problem-details body.</summary>
public static class ProblemMessages
{
    /// <summary>A message naming the limit or capability the server complained about, or the HTTP status.</summary>
    public static string Message(int status, JsonObject problem) => throw new PortPendingException("ProblemMessages.Message: jmap stream");
}
