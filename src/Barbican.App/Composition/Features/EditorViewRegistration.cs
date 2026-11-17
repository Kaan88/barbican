// Mirrors: M3 plan section 4.7. Owner editor-view: the editor control and its overlays (views, resource dictionary, the slot filler for the composer body); the view models come from M3PresentationRegistration.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The native editor's view layer (M3 stream editor-view).</summary>
public static class EditorViewRegistration
{
    public static IServiceCollection AddBarbicanEditorView(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddBarbicanEditorViewModels();
    }
}
