// Mirrors: M2 plan section 3.3. Owner menus: ICommandRegistry (Replace if needed), IContextMenuService (Replace), IDialogService, the menu providers, IPaletteProvider, the menu host, dialog host, palette and
// shortcuts-sheet IShellOverlay, the AccountHeaderSlot filler. Stage F creates it empty.

using Barbican.App.Shell;
using Barbican.App.Views.Dialogs;
using Barbican.App.Views.Menus;
using Barbican.App.Views.Palette;
using Barbican.App.Views.Shortcuts;
#if AUTOMATION
using Barbican.App.Views.Menus.Automation;
#endif
using Barbican.Presentation.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.App.Composition.Features;

/// <summary>Context menus, dialog host, account menu, rail, command palette, shortcuts (M2 stream menus).</summary>
public static class MenusRegistration
{
    // overlay order: the toast host (actions) sits below all of these; the menu is on top so it can be opened over the sheet or the palette is never needed (they are modal)
    private const int ShortcutsZ = 40;
    private const int PaletteZ = 50;
    private const int DialogZ = 60;
    private const int MenuZ = 70;

    public static IServiceCollection AddBarbicanMenus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBarbicanMenusPresentation();
        services.TryAddSingleton<ContextPointerRouter>();
        services.AddSingleton<IShellOverlay>(_ => new ViewOverlay(() => new ShortcutSheetView(), ShortcutsZ));
        services.AddSingleton<IShellOverlay>(_ => new ViewOverlay(() => new PaletteView(), PaletteZ));
        services.AddSingleton<IShellOverlay>(_ => new ViewOverlay(() => new DialogHostView(), DialogZ));
        services.AddSingleton<IShellOverlay>(_ => new ViewOverlay(() => new MenuHostView(), MenuZ));
#if AUTOMATION
        services.AddBarbicanMenusAutomation();
#endif
        return services;
    }
}
