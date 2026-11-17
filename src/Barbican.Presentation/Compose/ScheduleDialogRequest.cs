// Mirrors: Rust schedule.rs:104 open_dialog (Reschedule of the Scheduled view) and the composer's Schedule send; I-241. A dialog request that draws the ScheduleDialogViewModel of send-pipeline in the dialog host. Owner composer-ui.

using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Compose;

/// <summary>The schedule dialog outside a composer (the model completes with the chosen time).</summary>
public sealed record ScheduleDialogRequest(ScheduleDialogViewModel Model) : DialogRequest<bool>(Model.Title);
