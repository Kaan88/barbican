#if WINDOWS
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Plugins;

internal sealed record PluginFilePreviewRequest(string Name, UIElement Preview) : DialogRequest<bool>(Name);
#endif
