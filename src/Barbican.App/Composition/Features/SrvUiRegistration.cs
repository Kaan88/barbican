// Mirrors: M6 plan section 4.9 (DI). Owner srv-ui: one ISettingsPageFactory per server-backed page (identities, templates, organize, filters, vacation, recover), the rule editor and the dialogs. Until a factory is registered the screen shows that page's generic schema rows.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The server-backed settings pages (M6 stream srv-ui).</summary>
public static class SrvUiRegistration
{
    public static IServiceCollection AddBarbicanServerUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanServerUiViewModels();
        return services;
    }
}
