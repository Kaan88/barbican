// Mirrors: M3 plan section 4.7 (DI skeleton). Owners: editor-view (IEditorViewModelFactory, IRewriteProvider) and composer-ui (IComposerService, IRecipientSources); each stream edits only its own lines.

using Barbican.Core.Compose.Recipients;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Editor;
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

    public static IServiceCollection AddBarbicanComposerViewModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IComposerService, ComposerService>();
        services.TryAddSingleton<IRecipientSources, EmptyRecipientSources>();
        return services;
    }
}
