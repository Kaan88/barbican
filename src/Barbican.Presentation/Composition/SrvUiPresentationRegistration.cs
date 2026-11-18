// Mirrors: M6 plan section 4.9 (DI). Owner srv-ui: the view models of the server-backed settings pages.

using Barbican.Presentation.Settings.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M6 stream srv-ui.</summary>
public static class SrvUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanServerUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISrvUiFiltersViewModel, SrvUiFiltersViewModel>();
        services.TryAddSingleton<SrvUiVacationViewModel>();
        services.TryAddSingleton<SrvUiIdentitiesViewModel>();
        services.TryAddSingleton<SrvUiTemplatesViewModel>();
        return services;
    }
}
