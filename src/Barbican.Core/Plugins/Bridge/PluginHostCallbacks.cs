// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:118-145 (encodeCallbacks: a function handed to a frame becomes a { __pluginCallback: id } marker held in a table), :521-537 (callback-invoke: the frame calls it by id,
// an unknown id answers an error) and runtime.tsx:420-432,437-455 (the frame side: the marker becomes a function that sends callback-invoke).
// A table belongs to one frame. The host registers what it wants the frame to be able to call before the frame starts; the frame can call nothing else, and each handler checks what it is handed.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Bridge;

/// <summary>The host functions one add-on frame may call through callback-invoke, by marker id.</summary>
public sealed class PluginHostCallbacks
{
    /// <summary>How many callbacks one frame may be given; a host wires a handful.</summary>
    public const int MaxCallbacks = 16;

    /// <summary>The property of a callback marker (Bulwark host-bridge.ts encodeCallbacks, runtime.tsx decodeCallbacks).</summary>
    public const string MarkerKey = "__pluginCallback";

    private readonly ConcurrentDictionary<string, Func<PluginValue, CancellationToken, Task<PluginValue?>>> _handlers = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="handler"/>, which receives the array of arguments the frame sent and returns the value the frame's call resolves to (null: undefined). Returns the marker id.</summary>
    public string Register(Func<PluginValue, CancellationToken, Task<PluginValue?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (_handlers.Count >= MaxCallbacks) throw new InvalidOperationException("Too many callbacks.");
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        _ = _handlers.TryAdd(id, handler);
        return id;
    }

    /// <summary>The handler behind <paramref name="id"/>, or null for an id this table never issued (or one that was cleared).</summary>
    public Func<PluginValue, CancellationToken, Task<PluginValue?>>? Find(string id) =>
        id is not null && _handlers.TryGetValue(id, out var handler) ? handler : null;

    /// <summary>Forgets every callback: the frame's calls after this answer "unknown callback".</summary>
    public void Clear() => _handlers.Clear();

    /// <summary>The value a frame receives for a callback: <c>{ __pluginCallback: id }</c>.</summary>
    public static int Marker(PluginValueBuilder builder, string id)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Object((MarkerKey, builder.String(id)));
    }
}
