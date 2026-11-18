// Mirrors: M6 plan section 4.9 (DI). Owner srv-data: filters, vacation, folder management and the templates manager. Defaults use TryAddSingleton; the stubs have no constructor parameters until srv-data replaces them.

using Barbican.Core.Settings.Folders;
using Barbican.Core.Settings.Templates;
using Barbican.Core.Settings.Vacation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Filters.Rules;

/// <summary>Registers the server-backed settings data layer (M6 stream srv-data).</summary>
public static class FilterDataRegistration
{
    public static IServiceCollection AddBarbicanServerSettingsData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFilterService, FilterService>();
        services.TryAddSingleton<IVacationService, VacationService>();
        services.TryAddSingleton<IFolderAdmin, FolderAdmin>();
        services.TryAddSingleton<ITemplateAdmin, TemplateAdmin>();
        return services;
    }
}
