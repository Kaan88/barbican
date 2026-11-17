// Mirrors: RFC 8620 §3.6.2 (method-level errors, the "error" response).

namespace Barbican.Core.Jmap.Errors;

/// <summary>A method-level error (<c>["error", {type, description}, callId]</c>).</summary>
public sealed record MethodError(string Method, string Type, string? Description) : JmapError($"{Method}: {Type}");
