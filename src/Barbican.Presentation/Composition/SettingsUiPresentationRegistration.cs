// Mirrors: M6 plan section 4.9 (DI). Owner set-ui: the settings screen's view model.

using Barbican.Presentation.Settings.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M6 stream set-ui.</summary>
public static class SettingsUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanSettingsUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISetUiViewModel, SetUiViewModel>();
        return services;
    }
}
