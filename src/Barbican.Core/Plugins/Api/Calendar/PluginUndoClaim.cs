// Mirrors: Bulwark assistant/c.undo.ts:46-65 withUndoToken (spent on success, kept on failure).
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginUndoClaim(Lock gate, Dictionary<string, PluginUndoEntry> tokens, string token, PluginUndoEntry entry)
{
    internal JsonObject Data => entry.Data;

    /// <summary>The undo went through: the token is gone.</summary>
    internal void Spend()
    {
        lock (gate)
        {
            _ = tokens.Remove(token);
            entry.Busy = false;
        }
    }

    /// <summary>The undo failed: the token stays for another try.</summary>
    internal void Release()
    {
        lock (gate) entry.Busy = false;
    }
}
