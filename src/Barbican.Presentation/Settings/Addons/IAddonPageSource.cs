// Mirrors: nothing in Bulwark (native add-on host services, decision D-M6b-08).

namespace Barbican.Presentation.Settings.Addons;

/// <summary>Finds the settings page an enabled add-on contributes.</summary>
public interface IAddonPageSource
{
    /// <summary>The page of the add-on, or null when it is not running (disabled, still starting, failed) or has neither settings nor chat.</summary>
    AddonPage? Find(string pluginId);
}
