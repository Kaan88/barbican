// Mirrors: M5 plan section 4.10 (DI). Owner files-ui: the files view model.

using Barbican.Presentation.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M5 stream files-ui.</summary>
public static class FilesUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanFilesUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFilesViewModel, FilesViewModel>();
        return services;
    }
}
