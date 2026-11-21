// Mirrors: Bulwark components/email/email-composer.tsx:6803-6879 (the schedule dialog, the send warnings and the close dialog are Dialogs of the one dialog shell, ui/dialog.tsx), external-recipient-confirm.tsx;
// PORT-PLAN section 3.7 (one in-window dialog host, never ContentDialog); CP-07.
// The questions of the composer (ComposerViewModel.Dialog, ComposerViewModel.Schedule) are shown by the shared dialog host. The view model owns the answer: this only shows the question and hands back
// what the host returned. A question that ends any other way than through its own go-ahead button (Escape, the view going away, another composer replacing this one) answers null, which every caller
// in ComposerViewModel treats as "do not send" / "do not close" / "do not discard".

using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Compose;

public sealed partial class ComposerView
{
    private CancellationTokenSource? _questionCts;
    private object? _questionShown;
    private DialogService? _windowDialogs;
    private DialogHostView? _windowHost;
    private XamlRoot? _windowRoot;

    // shows the question the view model holds, or closes the one on screen when it holds none (the view model completed it itself)
    private void SyncQuestion()
    {
        object? want = _vm?.Dialog ?? (object?)_vm?.Schedule;
        if (ReferenceEquals(want, _questionShown))
        {
            return;
        }

        CancelQuestion();
        _questionShown = want;
        if (want is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _questionCts = cts;
        _ = RunQuestionAsync(want, cts.Token);
    }

    private void CancelQuestion()
    {
        _questionCts?.Cancel();
        _questionCts = null;
        _questionShown = null;
    }

    private async Task RunQuestionAsync(object question, CancellationToken ct)
    {
        var dialogs = DialogHostRouter.For(this);
        try
        {
            switch (question)
            {
                case ComposerDialog dialog:
                    var choice = await dialogs.ShowAsync(new ComposerQuestionRequest(dialog), ct);
                    if (!ct.IsCancellationRequested)
                    {
                        dialog.Complete(choice);
                    }

                    break;
                case ScheduleDialogViewModel schedule:
                    _ = await dialogs.ShowAsync(new ScheduleDialogRequest(schedule), ct);
                    if (!ct.IsCancellationRequested)
                    {
                        // the host closed it without a time (Escape): a cancel
                        schedule.Cancel();
                    }

                    break;
                default:
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // the view model ended the question itself
        }
    }

    // a composer in a window of its own (email-composer.tsx:5121-5131) has no shell overlay: it carries a dialog host over its own service, which DialogHostRouter hands to the questions
    private void UseWindowDialogs(bool detached)
    {
        if (detached == (_windowHost is not null))
        {
            return;
        }

        if (detached)
        {
            var service = new DialogService();
            var host = new DialogHostView(service, null);
            _windowDialogs = service;
            _windowHost = host;
            host.Loaded += (_, _) =>
            {
                if (host.XamlRoot is { } root && !ReferenceEquals(root, _windowRoot))
                {
                    _windowRoot = root;
                    DialogHostRouter.Register(root, service);
                }
            };
            Host.Children.Add(host);
            return;
        }

        if (_windowRoot is { } registered)
        {
            DialogHostRouter.Unregister(registered);
        }

        _windowRoot = null;
        if (_windowHost is { } leaving)
        {
            _ = Host.Children.Remove(leaving);
        }

        _windowHost = null;
        _windowDialogs?.Dispose();
        _windowDialogs = null;
    }
}
