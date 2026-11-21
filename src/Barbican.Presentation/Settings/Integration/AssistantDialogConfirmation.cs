// Mirrors: Rust agent.rs Host::confirm; B-AI-009; M6 plan D-7.
// M6 integration: pool-thread tool requests are marshalled to the in-window dialog.

using Barbican.Core.Settings.Assistant;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Settings.Integration;

/// <summary>Asks through the existing application dialog on its dispatcher.</summary>
public sealed class AssistantDialogConfirmation(IDispatcher dispatcher, IActionConfirmer? confirmer = null) : IAssistantConfirmation
{
    public async Task<bool> ConfirmAsync(string tool, string description, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);
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
            accepted = await confirmer.ConfirmAsync(new ConfirmRequest("Allow assistant action?", description, "Allow"), ct).ConfigureAwait(true);
        }).ConfigureAwait(false);
        return accepted;
    }
}
