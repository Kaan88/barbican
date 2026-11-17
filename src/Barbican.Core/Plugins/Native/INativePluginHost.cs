// Mirrors: M2 plan 4.6 (INativePluginHost); Rust plugins.rs (email_details). Stub contract from stage F of M2; owner unified (budgets and fail-closed flags: R2-10).

using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>The host of the native .NET plugins.</summary>
public interface INativePluginHost
{
    /// <summary>The loaded plugins.</summary>
    IReadOnlyList<PluginInfo> Plugins { get; }

    /// <summary>The rows every plugin that handles email.details adds for a message (a handler that throws or is late adds none).</summary>
    Task<IReadOnlyList<EmailDetailRow>> EmailDetailsAsync(JsonNode email, CancellationToken ct);
}
