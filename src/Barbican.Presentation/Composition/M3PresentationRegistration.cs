// Mirrors: M3 plan section 4.7 (DI skeleton). Owners: editor-view (IEditorViewModelFactory, IRewriteProvider) and composer-ui (IComposerService, IRecipientSources); each stream edits only its own lines.

using Barbican.Core.Compose.Recipients;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Editor;
using Barbican.Presentation.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The portable (UI-free) services of the M3 streams editor-view and composer-ui.</summary>
public static class M3PresentationRegistration
{
    public static IServiceCollection AddBarbicanEditorViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEditorViewModelFactory, EditorViewModelFactory>();
        services.TryAddSingleton<IRewriteProvider, NoRewriteProvider>();
        return services;
    }

    /// <summary>The editor's commands (<c>rte.*</c>), its context menu providers and the active-editor holder; registered by the App head with the control.</summary>
    public static IServiceCollection AddBarbicanEditorCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ActiveEditor>();
        foreach (var index in Enumerable.Range(0, Enum.GetValues<EditorCommand>().Length))
        {
            var i = index;
            services.AddSingleton<IAppCommand>(sp => EditorCommands.Create(sp.GetRequiredService<ActiveEditor>())[i]);
        }

        foreach (var index in Enumerable.Range(0, EditorMenus.ProviderCount))
        {
            var i = index;
            services.AddSingleton<IMenuProvider>(sp => EditorMenus.Providers(sp.GetRequiredService<ActiveEditor>())[i]);
        }
        return services;
    }

    public static IServiceCollection AddBarbicanComposerViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IComposerService, ComposerService>();
        services.TryAddSingleton<IRecipientSources, EmptyRecipientSources>();
        return services;
    }
}
