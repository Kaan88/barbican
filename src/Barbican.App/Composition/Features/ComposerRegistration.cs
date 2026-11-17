// Mirrors: M3 plan section 4.7. Owner composer-ui: the composer shell views, recipient chips, dialogs, the compose commands and shortcuts, mailto and single-instance start-up; the view models come from M3PresentationRegistration.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The composer shell (M3 stream composer-ui).</summary>
public static class ComposerRegistration
{
    public static IServiceCollection AddBarbicanComposer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddBarbicanComposerViewModels();
    }
}
