// Mirrors: Rust agent.rs Host::confirm (B-AI-009), generalized (decision D-M6b-08).

namespace Barbican.Plugins;

/// <summary>Asks the user in the app's own dialog.</summary>
public interface IPluginDialogs
{
    /// <summary>Shows what is about to happen; true when the user agrees. Safe to call from any thread; false when the app cannot ask.</summary>
    Task<bool> ConfirmAsync(string title, string description, CancellationToken ct);
}
