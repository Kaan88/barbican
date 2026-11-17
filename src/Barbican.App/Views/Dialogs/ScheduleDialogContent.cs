// Mirrors: Bulwark components/email/schedule-send-dialog.tsx; I-241. Draws ScheduleDialogRequest inside the dialog host: the same card the composer shows, over the ScheduleDialogViewModel of send-pipeline.

using Barbican.App.Views.Compose;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Dialogs;

/// <summary>The schedule dialog of the Scheduled view.</summary>
public sealed class ScheduleDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is ScheduleDialogRequest;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var model = ((ScheduleDialogRequest)dialog.Request).Model;
        _ = model.Completion.ContinueWith(_ => dialog.Complete(true), TaskScheduler.FromCurrentSynchronizationContext());
        return ComposerView.ScheduleCard(model);
    }
}
