#if AUTOMATION
// Mirrors: spec/automation-protocol.md `toast-last`: the text (and the action label) of the toast shown last, even when it has left the screen.

using Barbican.Automation;
using Barbican.Presentation.Actions;

namespace Barbican.App.Views.Actions.Automation;

/// <summary>The <c>toast-last</c> command.</summary>
public sealed class ToastLastCommand(ToastService toasts) : IAutomationCommand
{
    public string Name => "toast-last";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        if (toasts.LastShown is not { } toast) return Task.FromResult("none");
        if (string.Equals(args.Trim(), "source", StringComparison.Ordinal))
            return Task.FromResult($"ok {toast.DisplaySource}|{toast.Severity}|{toast.Message}");
        var visibility = toasts.Toasts.Contains(toast) ? "visible" : "gone";
        return Task.FromResult($"ok {toast.Message}|{toast.ActionLabel}|{(toast.IsError ? "error" : "info")}|{visibility}");
    }
}
#endif
