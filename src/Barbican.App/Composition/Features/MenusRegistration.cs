// Mirrors: M2 plan section 3.3. Owner menus: ICommandRegistry (Replace if needed), IContextMenuService (Replace), IDialogService, the menu providers, IPaletteProvider, the menu host, dialog host, palette and
// shortcuts-sheet IShellOverlay, the AccountHeaderSlot filler. Stage F creates it empty.

using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>Context menus, dialog host, account menu, rail, command palette, shortcuts (M2 stream menus).</summary>
public static class MenusRegistration
{
    public static IServiceCollection AddBarbicanMenus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
