// Mirrors: M5 plan section 4.10 (DI). Owner files-ui: the files view model.

using Barbican.Presentation.Commands;
using Barbican.Presentation.Files;
using Barbican.Presentation.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable services of M5 stream files-ui.</summary>
public static class FilesUiPresentationRegistration
{
    public static IServiceCollection AddBarbicanFilesUiViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<FilesViewModel>();
        services.TryAddSingleton<IFilesViewModel>(sp => sp.GetRequiredService<FilesViewModel>());
        services.TryAddSingleton<IFilesPicker, FilesPicker>();
        services.TryAddSingleton(sp => (FilesPicker)sp.GetRequiredService<IFilesPicker>());
        services.TryAddSingleton<FilesActions>();
        services.TryAddSingleton(sp => new FilesCommandSet(FilesCommands.Create(sp.GetRequiredService<FilesActions>())));
        for (var i = 0; i < FilesCommands.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAppCommand>(sp => sp.GetRequiredService<FilesCommandSet>().Commands[index]);
        }

        services.TryAddSingleton(sp => new FilesMenuSet(FilesMenus.Create(sp.GetRequiredService<FilesActions>())));
        for (var i = 0; i < FilesMenus.Count; i++)
        {
            var index = i;
            services.AddSingleton<IMenuProvider>(sp => sp.GetRequiredService<FilesMenuSet>().Providers[index]);
        }
        return services;
    }
}
