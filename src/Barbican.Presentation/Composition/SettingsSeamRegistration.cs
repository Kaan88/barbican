// Mirrors: M6 plan section 4.1 (DI of the seam both UI streams share). Frozen in the M6 plan commit; owner integrator.

using Barbican.Presentation.Settings;
using Barbican.Core.Plugins.Native.Host;
using Barbican.Plugins;
using Barbican.Presentation.Settings.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>Registers the settings navigation (M6 plan commit).</summary>
public static class SettingsSeamRegistration
{
    public static IServiceCollection AddBarbicanSettingsSeam(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISettingsNavigation, SettingsNavigation>();
        services.TryAddSingleton<IPluginDialogs, PluginDialogConfirmation>();
        services.TryAddSingleton<IPluginComposer, PluginComposerBridge>();
        return services;
    }
}
