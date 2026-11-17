// Mirrors: RFC 8620 §3.7 (ResultReference: resultOf, name, path). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Requests;

/// <summary>A call in a request being built.</summary>
/// <param name="Method">The method name.</param>
/// <param name="CallId">The method call id.</param>
public sealed record CallHandle(string Method, string CallId)
{
    /// <summary>A result reference to this call's response at the JSON Pointer <paramref name="path"/> (wildcard <c>*</c> allowed).</summary>
    public JsonObject Ref(string path) => new() { ["resultOf"] = CallId, ["name"] = Method, ["path"] = path };
}
