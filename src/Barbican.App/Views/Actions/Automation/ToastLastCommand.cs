#if AUTOMATION
// Mirrors: spec/automation-protocol.md `toast-last`: the text (and the action label) of the toast shown last, even when it has left the screen.

using Barbican.Automation;
using Barbican.Presentation.Actions;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>toast-last</c> command.</summary>
public sealed class ToastLastCommand(ToastService toasts) : IAutomationCommand
{
    public string Name => "toast-last";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken) =>
        Task.FromResult(toasts.LastShown is { } t ? $"ok {t.Message}|{t.ActionLabel}|{(t.IsError ? "error" : "info")}|{(toasts.Toasts.Contains(t) ? "visible" : "gone")}" : "none");
}
#endif
