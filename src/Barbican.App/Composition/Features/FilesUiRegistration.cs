// Mirrors: M5 plan section 4.10 (DI). Owner files-ui: the files screen (registered as an IAppScreenFactory), the preview, details panel and dialogs, menus and commands.
// A sample-data run (BARBICAN_SAMPLE=1, Automation build) swaps the store, writer and transfer for an in-memory sample account and adds the files-* automation commands.

using Barbican.App.Shell;
using Barbican.App.Views.Dialogs;
using Barbican.App.Views.Files;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
#if AUTOMATION
using Microsoft.Extensions.DependencyInjection.Extensions;
using Barbican.App.Views.Files.Automation;
using Barbican.Automation;
using Barbican.Core.Files;
#endif

namespace Barbican.App.Composition.Features;

/// <summary>The files screen (M5 stream files-ui).</summary>
public static class FilesUiRegistration
{
    public static IServiceCollection AddBarbicanFilesUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanFilesUiViewModels();
        services.AddSingleton<IAppScreenFactory, FilesScreenFactory>();
        services.AddSingleton<IDialogContent, FileConflictDialogContent>();
        services.AddSingleton<IDialogContent, FileMoveDialogContent>();
#if AUTOMATION
        if (string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE"), "1", StringComparison.Ordinal))
        {
            services.AddSingleton<SampleFiles>();
            services.Replace(ServiceDescriptor.Singleton<IFileStore>(sp => sp.GetRequiredService<SampleFiles>()));
            services.Replace(ServiceDescriptor.Singleton<IFileWriter>(sp => sp.GetRequiredService<SampleFiles>()));
            services.Replace(ServiceDescriptor.Singleton<IFileTransfer>(sp => sp.GetRequiredService<SampleFiles>()));
        }

        for (var i = 0; i < FilesCommand.Count; i++)
        {
            var index = i;
            services.AddSingleton<IAutomationCommand>(sp => FilesCommand.Create(sp).ElementAt(index));
        }
#endif
        return services;
    }
}
