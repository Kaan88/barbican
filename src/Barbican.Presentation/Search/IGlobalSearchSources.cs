// Mirrors: Bulwark lib/global-search/providers/index.ts (the contacts, calendar and files providers beside mail).

namespace Barbican.Presentation.Search;

/// <summary>Where the palette's search finds contacts, events and files.</summary>
public interface IGlobalSearchSources
{
    /// <summary>The active login's contacts, events and files (loading a store that has not been read yet); empty parts for a login without the capability.</summary>
    Task<GlobalSearchData> LoadAsync(CancellationToken ct);
}
