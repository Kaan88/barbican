// Mirrors: Bulwark components/global-search/search-scope-chips.tsx:25-47 (the scope chips: All, Mail, Contacts, Calendar, Files) and search-facets.tsx:60-78 (the count of each kind, "+" when the server had more).

namespace Barbican.Presentation.Palette;

/// <summary>One scope chip of a palette scope: a kind of result with how many the search found.</summary>
/// <param name="Key">The stable key ("all", "mail", "contacts", "calendar", "files").</param>
/// <param name="Label">The visible text.</param>
/// <param name="Count">How many results of this kind the search found (all kinds for "all").</param>
/// <param name="More">The server had more than were fetched (the count shows a "+").</param>
/// <param name="Active">The chip is on.</param>
public sealed record PaletteFacet(string Key, string Label, int Count, bool More, bool Active);
