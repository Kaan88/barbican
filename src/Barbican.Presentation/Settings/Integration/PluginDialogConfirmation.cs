// Mirrors: Rust agent.rs Host::confirm; B-AI-009; M6 plan D-7; decision D-M6b-08.
// Integration: pool-thread add-on requests are marshalled to the in-window dialog.

using Barbican.Plugins;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Integration;

/// <summary>Asks through the existing application dialog on its dispatcher.</summary>
public sealed class PluginDialogConfirmation(IDispatcher dispatcher, IActionConfirmer? confirmer = null) : IPluginDialogs
{
    public async Task<bool> ConfirmAsync(string title, string description, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(description);
        ct.ThrowIfCancellationRequested();
        if (confirmer is null)
        {
            return false;
        }

        var accepted = false;
        await dispatcher.InvokeAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            accepted = await confirmer.ConfirmAsync(new ConfirmRequest(title, description, "Allow"), ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
        return accepted;
    }
}
