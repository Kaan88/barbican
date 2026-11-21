// Mirrors: Bulwark components/global-search/global-search-palette.tsx:215-225 (SearchScopeChips under the input; the chip sets the store's scope, an in: operator in the query wins).

namespace Barbican.Presentation.Palette;

/// <summary>A palette provider that narrows its rows by scope chips (global search: All, Mail, Contacts, Calendar, Files).</summary>
public interface IPaletteFacetProvider : IPaletteProvider
{
    /// <summary>The chips with the counts of the last search and the one that is on.</summary>
    IReadOnlyList<PaletteFacet> Facets { get; }

    /// <summary>Turns a chip on; the next <see cref="IPaletteProvider.RowsAsync"/> shows only that kind. An unknown key changes nothing.</summary>
    void SetFacet(string key);

    /// <summary>The key of the chip <paramref name="delta"/> steps from the one that is on (wrapping).</summary>
    string NextFacetKey(int delta);
}
