// Mirrors: M5 plan section 4.12 (the shell seam: ShellPage shows the screen of the factory registered for the rail's active app) and M6 plan section 4.1. The plan commit lands this minimal working screen so both UI streams build on a real seam: a page list on the left (SettingsNav), the page of the registered ISettingsPageFactory on the right (built on the first visit, then only hidden),
// and a plain notice for a page without a factory until set-ui builds the generic schema pages. set-ui owns this file and the screen it creates for the whole milestone.

using Barbican.App.Settings;
using Barbican.App.Shell;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Shell;

namespace Barbican.App.Views.Settings;

/// <summary>The Settings app of the rail.</summary>
public sealed class SetUiScreenFactory : IAppScreenFactory
{
    private SetUiScreen? _screen;

    public AppId App => AppId.Settings;

    public bool IsAvailable() => true;

    public UIElement Create() => _screen ??= new SetUiScreen(ViewHost.Get<ISettingsNavigation>(), ViewHost.Find<IEnumerable<ISettingsPageFactory>>()?.ToList() ?? []);

    public void OnShown() => _screen?.OnShown();
}
