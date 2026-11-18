// Mirrors: M5 plan section 4.10 (DI). Owner files-ui: the files screen (registered as an IAppScreenFactory), the preview, details panel and dialogs, menus and commands.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The files screen (M5 stream files-ui).</summary>
public static class FilesUiRegistration
{
    public static IServiceCollection AddBarbicanFilesUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanFilesUiViewModels();
        return services;
    }
}
