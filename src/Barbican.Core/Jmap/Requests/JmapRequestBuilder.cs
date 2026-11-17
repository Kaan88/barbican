// Mirrors: RFC 8620 §3.2 (Invocation: [name, arguments, method call id]), §3.7 (result references `#arg`: resultOf, name, path),
// §5.3 (creation ids referenced as `#cid`); Rust jmap.rs:524 list_page and :592 list_delta (the calls they chain). Owner jmap.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Requests;

/// <summary>Builds a request call by call; each call gets the next call id ("0", "1", ...).</summary>
public sealed class JmapRequestBuilder
{
    private readonly List<JsonArray> _calls = [];

    /// <summary>The calls added so far.</summary>
    public int Count => _calls.Count;

    /// <summary>Adds a call; the handle names it for result references and for reading its response.</summary>
    public CallHandle Add(string method, JsonObject args)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(args);
        var id = _calls.Count.ToString(CultureInfo.InvariantCulture);
        _calls.Add(JmapRequest.Call(method, args, id));
        return new CallHandle(method, id);
    }

    /// <summary>The request (empty <paramref name="use"/>: every capability the server advertises).</summary>
    public JmapRequest Build(IReadOnlyList<string>? use = null) => new() { Using = use ?? [], MethodCalls = [.. _calls] };

    /// <summary>The reference to a creation id of an earlier <c>/set</c> in the same request (<c>"#cid"</c>).</summary>
    public static string CreationRef(string creationId) => "#" + creationId;
}
