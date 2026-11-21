// Mirrors: Bulwark assistant/c.undo.ts:13-21 (Entry).
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginUndoEntry(string pluginId, string accountKey, string kind, DateTimeOffset expires, JsonObject data, long order)
{
    internal string PluginId { get; } = pluginId;

    internal string AccountKey { get; } = accountKey;

    internal string Kind { get; } = kind;

    internal DateTimeOffset Expires { get; } = expires;

    internal JsonObject Data { get; } = data;

    internal long Order { get; } = order;

    /// <summary>Its undo is running: a second call is refused (c.undo.ts:19).</summary>
    internal bool Busy { get; set; }
}
