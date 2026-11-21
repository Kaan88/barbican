#if WINDOWS
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Plugins;

/// <summary>An add-on's ui.openDialog dialog for the dialog host. <paramref name="StartFrame"/> starts the slot frame in the panel the content gives it (with the theme of the area it sits in); <paramref name="TurnOff"/> is the
/// "Turn off" entry of the options menu (Bulwark plugin-dialog-host.tsx:583-621); <paramref name="Source"/> is the add-on's display name, supplied by the host.</summary>
internal sealed record PluginSlotDialogHostRequest(string Title, string Source, PluginSlotDialogRequest Dialog,
    Func<Panel, System.Text.Json.JsonElement, CancellationToken, Task<Result<IPluginSlotFrameInstance>>> StartFrame, Func<Task>? TurnOff = null)
    : DialogRequest<PluginSlotDialogOutcome>(Title);
#endif
