// Mirrors: M2 plan section 3.3. Owner search (wave 2): IListQueryShaper (Replace), ISnippetSource, the search commands, IPaletteProvider for global search, the quick rule commands. Stage F registers the one
// slot filler for the search box it moved into SearchBoxView; the owner keeps it.

using Barbican.App.Shell;
using Barbican.App.Views.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Composition.Features;

/// <summary>Search syntax, chips, suggestions, history, snippets, filter panel, global search, quick rules (M2 stream search).</summary>
public static class SearchRegistration
{
    public static IServiceCollection AddBarbicanSearch(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISlotContent>(new SlotContent(SlotNames.ListSearch, () => new SearchBoxView()));
        return services;
    }
}
