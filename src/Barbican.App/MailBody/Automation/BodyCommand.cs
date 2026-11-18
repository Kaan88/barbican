#if AUTOMATION
// Mirrors: spec/automation-protocol.md (M1 body rows): `body state|load-external|show-formatted` and Rust automation.rs `load-images`,
// mailview.rs state :520 / shell_ready :517 (tag rust-final). Read-only probes of the body view; nothing is written anywhere. Owner body.

using System.Globalization;
using Barbican.Automation;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>body</c> command: the body view's state, and its two per-message actions.</summary>
public sealed class BodyCommand(BodyShell shell, ReaderViewModel reader, MessageLoader loader, IDispatcher dispatcher) : IAutomationCommand
{
    public string Name => "body";

    public Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        switch ((args ?? string.Empty).Trim())
        {
            case "":
            case "state":
                return Task.FromResult(string.Create(
                    CultureInfo.InvariantCulture,
                    $"shell {(shell.IsShellUp ? "up" : "down")} seq {shell.DocumentSequence} shown {shell.ShownId ?? "-"} loading {Yes(shell.IsLoading)} remote-blocked {Yes(shell.RemoteBlocked)} plain {Yes(shell.ShownAsPlainText)} dropped {shell.DroppedMessages} links {shell.RoutedLinks} blocked {shell.BlockedNavigations} filtered {shell.FilteredRequests}"));
            case "reader":
                return Task.FromResult($"reader {(reader.EmailId.Length == 0 ? "-" : reader.EmailId)} {reader.BodyState} conversation {reader.Conversation.Count} error {reader.Error ?? "-"}");
            case "timing":
                return Task.FromResult(string.Create(
                    CultureInfo.InvariantCulture,
                    $"open-to-shown {Ms(loader.LastOpenToShown)} send-to-ready {Ms(shell.LastSendToReady)} opens {loader.OpenCounts.Opens} cached {loader.OpenCounts.CachedOpens}"));
            case "background":
                return OnUiAsync(shell.Power.EnterBackground);
            case "foreground":
                return OnUiAsync(shell.Power.EnterForeground);
            case "power":
                return OnUiResultAsync(PowerState);
            case var other when other.StartsWith("close-after ", StringComparison.Ordinal) && int.TryParse(other[12..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds):
                // a test's own wait instead of the setting's minutes
                return OnUiAsync(() => shell.Power.CloseAfter = () => TimeSpan.FromSeconds(seconds));
            case "load-external":
                shell.LoadExternalContent();
                return Task.FromResult("ok");
            case "show-formatted":
                shell.ShowFormatted();
                return Task.FromResult("ok");
            default:
                return Task.FromResult("error body state|reader|timing|background|foreground|power|close-after SECONDS|load-external|show-formatted");
        }
    }

    // the policy and the WebView belong to the UI thread
    private async Task<string> OnUiAsync(Action work)
    {
        await dispatcher.InvokeAsync(() =>
        {
            work();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return "ok";
    }

    private async Task<string> OnUiResultAsync(Func<string> work)
    {
        var result = string.Empty;
        await dispatcher.InvokeAsync(() =>
        {
            result = work();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return result;
    }

    private string PowerState()
    {
        var p = shell.Power;
        var at = p.LastRecreated is { } t ? t.ToString("O", CultureInfo.InvariantCulture) : "-";
        return string.Create(CultureInfo.InvariantCulture, $"power {p.State} background {Yes(p.InBackground)} suspends {p.Suspends} closes {p.Closes} recreates {p.Recreates} recreated-at {at} shell {(shell.IsShellUp ? "up" : "down")} shown {shell.ShownId ?? "-"} suspend-result {shell.LastSuspend ?? "-"}");
    }

    private static string Yes(bool b) => b ? "yes" : "no";

    private static string Ms(TimeSpan? t) => t is { } v ? v.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) : "-";
}
#endif
