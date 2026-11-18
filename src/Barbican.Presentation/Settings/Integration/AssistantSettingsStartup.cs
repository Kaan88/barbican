// Loads the assistant settings cache off the UI thread when the app starts.
using Barbican.Core.Settings.Assistant;
using Barbican.Presentation.Composition;

namespace Barbican.Presentation.Settings.Integration;

/// <summary>Starts loading assistant settings before the page is normally opened.</summary>
public sealed class AssistantSettingsStartup(IAssistantSettings settings) : IStartupTask
{
    public Task StartAsync(CancellationToken cancellationToken) => settings.InitializeAsync(cancellationToken);
}
