// Mirrors: M6 plan section 4.9 (DI). Owner set-ui: the settings screen (registered as the IAppScreenFactory of the rail's Settings app), the generic rows, the account, security, assistant and backup pages, the assistant column.
// A sample-data run (BARBICAN_SAMPLE=1, Automation build) swaps the data services for the Sample* fakes of Barbican.Presentation.Settings.Sample (D-M6-00).

using Barbican.App.Shell;
using Barbican.App.Views.Settings;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The settings screen (M6 stream set-ui).</summary>
public static class SettingsUiRegistration
{
    public static IServiceCollection AddBarbicanSettingsUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanSettingsUiViewModels();
        services.AddSingleton<IAppScreenFactory, SetUiScreenFactory>();
        return services;
    }
}
