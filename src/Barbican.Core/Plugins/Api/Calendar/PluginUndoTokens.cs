// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.undo.ts:1-77 (a random token bound to the plugin and the signed-in account, 30 minutes, at most 50 per plugin,
// spent only when the undo went through, one undo at a time per token). What an undo needs stays in the host: the plugin never hands the host a copy to re-create.
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>Undo tokens for add-on deletes.</summary>
public sealed class PluginUndoTokens(TimeProvider time)
{
    /// <summary>How long a token can be used (c.undo.ts:11).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private const int MaxPerPlugin = 50;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PluginUndoEntry> _tokens = [];
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private long _counter;

    /// <summary>Keeps <paramref name="data"/> for an undo of <paramref name="kind"/> and returns its token.</summary>
    internal string Issue(string pluginId, string accountKey, string kind, JsonObject data)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var expired in _tokens.Where(t => t.Value.Expires <= now).Select(t => t.Key).ToArray()) _ = _tokens.Remove(expired);
            var mine = _tokens.Where(t => string.Equals(t.Value.PluginId, pluginId, StringComparison.Ordinal)).OrderBy(t => t.Value.Order).ToList();
            foreach (var old in mine.Take(Math.Max(0, mine.Count - MaxPerPlugin + 1))) _ = _tokens.Remove(old.Key);
            var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            _tokens[token] = new PluginUndoEntry(pluginId, accountKey, kind, now + Lifetime, data, ++_counter);
            return token;
        }
    }

    /// <summary>Claims the token for one undo, or null when it is unknown, expired, another plugin's, another login's, another kind or already being undone.</summary>
    internal PluginUndoClaim? Begin(string token, string pluginId, string accountKey, string kind)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var entry) || entry.Busy || entry.Expires <= _time.GetUtcNow()
                || !string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal) || !string.Equals(entry.AccountKey, accountKey, StringComparison.Ordinal)
                || !string.Equals(entry.Kind, kind, StringComparison.Ordinal))
            {
                return null;
            }

            entry.Busy = true;
            return new PluginUndoClaim(_gate, _tokens, token, entry);
        }
    }
}
