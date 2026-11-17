// Mirrors: M2 plan section 3.3. Owner unified (wave 2): IListSourceResolver (Replace), IMailTargetResolver, IAccountMaintenance, IUnifiedUnread, IMailImport, IAttachedMessageOpener, IAttachmentExpander,
// INativePluginHost, the sidebar section slot fillers. Stage F creates it empty.

using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>Unified inbox, shared accounts, account maintenance, import and export, native plugin host (M2 stream unified).</summary>
public static class UnifiedRegistration
{
    public static IServiceCollection AddBarbicanUnified(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
