#if WINDOWS
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Plugins;

/// <summary>An add-on dialog for the dialog host. <paramref name="TurnOff"/> is the "Turn off" entry of the dialog's options menu (Bulwark plugin-dialog-host.tsx:583-621): it disables the add-on, which closes the dialog. <paramref name="Title"/> is the title the add-on chose (or its name); <paramref name="Source"/> is the add-on's display name, supplied by the host.</summary>
internal sealed record PluginDialogHostRequest(string Title, string Source, PluginDialogRequest Dialog, Func<Task>? TurnOff = null) : DialogRequest<PluginDialogOutcome>(Title);
#endif
