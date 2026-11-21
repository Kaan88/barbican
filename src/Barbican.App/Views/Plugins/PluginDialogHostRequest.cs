#if WINDOWS
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Plugins;

/// <summary>An add-on dialog for the dialog host. <paramref name="Title"/> is the title the add-on chose (or its name); <paramref name="Source"/> is the add-on's display name, supplied by the host.</summary>
internal sealed record PluginDialogHostRequest(string Title, string Source, PluginDialogRequest Dialog) : DialogRequest<PluginDialogOutcome>(Title);
#endif
