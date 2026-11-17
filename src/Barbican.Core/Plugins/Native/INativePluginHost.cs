// Mirrors: M2 plan 4.6 (INativePluginHost); Rust plugins.rs:64 load_all (plugins and load errors), :107 email_details. Owner unified (M2 wave 2; budgets
// and fail-closed flags: R2-10, in HookPolicies and NativePluginHost).

using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>The host of the native .NET plugins.</summary>
public interface INativePluginHost
{
    /// <summary>The loaded plugins (disabled ones too, marked).</summary>
    IReadOnlyList<PluginInfo> Plugins { get; }

    /// <summary>What could not be loaded, one line per plugin folder ("folder: reason").</summary>
    IReadOnlyList<string> LoadErrors { get; }

    /// <summary>The rows every plugin that handles email.details adds for a message (a handler that throws or is late adds none).</summary>
    Task<IReadOnlyList<EmailDetailRow>> EmailDetailsAsync(JsonNode email, CancellationToken ct);
}
