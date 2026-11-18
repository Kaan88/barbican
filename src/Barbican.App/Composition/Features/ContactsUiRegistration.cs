// Mirrors: M5 plan section 4.10 (DI). Owner con-ui: the contacts screen (registered as an IAppScreenFactory), the form and dialog host, menus and commands. Nothing to register until the screen exists; calls the view model registration.

using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>The contacts screen (M5 stream con-ui).</summary>
public static class ContactsUiRegistration
{
    public static IServiceCollection AddBarbicanContactsUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _ = services.AddBarbicanContactsUiViewModels();
        return services;
    }
}
