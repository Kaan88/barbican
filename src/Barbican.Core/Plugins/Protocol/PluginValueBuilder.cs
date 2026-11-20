// Mirrors: Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666; the tagged v1 graph is a native transport adaptation.
#pragma warning disable CA1720 // Factory names mirror JavaScript structured-clone values.
using System.Globalization;
using System.Numerics;

namespace Barbican.Core.Plugins.Protocol;

/// <summary>Constructs a graph privately; Build snapshots the graph and copies bytes. Instances require no disposal.</summary>
public sealed class PluginValueBuilder
{
    private readonly List<PluginNode?> _nodes = [];
    private readonly Dictionary<PluginNode[], Dictionary<int, int>> _imports = new(ReferenceEqualityComparer.Instance);
    public int Reserve() { _nodes.Add(null); return _nodes.Count - 1; }
    private int Add(PluginNode node) { var id = Reserve(); _nodes[id] = node; return id; }
    public int Undefined() => Add(new(PluginValueKind.Undefined));
    public int Null() => Add(new(PluginValueKind.Null));
    public int Boolean(bool value) => Add(new(PluginValueKind.Boolean, Flag: value));
    public int Number(double value) => Add(new(PluginValueKind.Number, Text: value.ToString("R", CultureInfo.InvariantCulture)));
    public int String(string value) => Add(new(PluginValueKind.String, Text: value ?? throw new ArgumentNullException(nameof(value))));
    public int Buffer(ReadOnlySpan<byte> bytes) => Add(new(PluginValueKind.ArrayBuffer, Bytes: bytes.ToArray()));
    public int Blob(ReadOnlySpan<byte> bytes, string type) => Add(new(PluginValueKind.Blob, Text: type ?? throw new ArgumentNullException(nameof(type)), Bytes: bytes.ToArray()));
    public int View(int buffer, PluginViewType type, int offset, int length) => Add(new(PluginValueKind.View, Links: [buffer], View: type, Offset: offset, Length: length));
    public int Date(double unixMilliseconds) => Add(new(PluginValueKind.Date, Text: unixMilliseconds.ToString("R", CultureInfo.InvariantCulture)));
    public int BigInt(BigInteger value) => Add(new(PluginValueKind.BigInt, Text: value.ToString(CultureInfo.InvariantCulture)));
    public int Error(string name, string message, string? stack = null, int? cause = null) => Add(new(PluginValueKind.Error, Keys: [name ?? throw new ArgumentNullException(nameof(name)), message ?? throw new ArgumentNullException(nameof(message)), stack ?? string.Empty], Flag: stack is not null, Links: cause is null ? [] : [cause.Value]));
    public int Array(params int[] items) => Add(new(PluginValueKind.Array, Links: (int[])items.Clone()));
    public int Object(params (string Key, int Value)[] properties) => Add(new(PluginValueKind.Object, Keys: [.. properties.Select(p => p.Key)], Links: [.. properties.Select(p => p.Value)]));
    public int Map(params (int Key, int Value)[] entries) => Add(new(PluginValueKind.Map, Links: [.. entries.SelectMany(p => new[] { p.Key, p.Value })]));
    public int Set(params int[] items) => Add(new(PluginValueKind.Set, Links: (int[])items.Clone()));
    public void SetArray(int id, params int[] items) => SetReserved(id, new(PluginValueKind.Array, Links: (int[])items.Clone()));
    public void SetObject(int id, params (string Key, int Value)[] properties) => SetReserved(id, new(PluginValueKind.Object, Keys: [.. properties.Select(p => p.Key)], Links: [.. properties.Select(p => p.Value)]));
    public void SetMap(int id, params (int Key, int Value)[] entries) => SetReserved(id, new(PluginValueKind.Map, Links: [.. entries.SelectMany(p => new[] { p.Key, p.Value })]));
    public void SetSet(int id, params int[] items) => SetReserved(id, new(PluginValueKind.Set, Links: (int[])items.Clone()));
    private void SetReserved(int id, PluginNode node)
    {
        if (id < 0 || id >= _nodes.Count || _nodes[id] is not null) throw new ArgumentException("Node is not reserved.", nameof(id));
        _nodes[id] = node;
    }
    /// <summary>Imports only reachable nodes, preserving aliases across imports from the same source graph.</summary>
    public int Import(PluginValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var source = value.Nodes;
        if (!_imports.TryGetValue(source, out var map))
        {
            map = [];
            _imports.Add(source, map);
        }
        var queue = new Queue<int>();
        int ReserveSource(int id)
        {
            if (map.TryGetValue(id, out var existing)) return existing;
            var destination = Reserve();
            map.Add(id, destination);
            queue.Enqueue(id);
            return destination;
        }
        var root = ReserveSource(value.Id);
        while (queue.TryDequeue(out var id))
        {
            var node = source[id];
            var links = node.Links?.Select(ReserveSource).ToArray();
            _nodes[map[id]] = node with { Links = links };
        }
        return root;
    }
    internal int AddRaw(PluginNode node) => Add(node);
    public PluginValue Build(int root, PluginValueLimits? limits = null)
    {
        var policy = limits ?? new();
        if (_nodes.Count > policy.MaxNodes || _nodes.Any(n => n is null)) throw new PluginValueFormatException("Too many or unfilled graph nodes.");
        var source = _nodes.Select(n => n!).ToArray();
        PluginValueCodec.Validate(source, root, policy);
        var nodes = source.Select(n => n with { Bytes = n.Bytes?.ToArray(), Links = n.Links?.ToArray(), Keys = n.Keys?.ToArray() }).ToArray();
        return new PluginValue(nodes, root, policy);
    }
}
