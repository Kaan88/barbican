// Mirrors: Bulwark components/global-search/search-preview.tsx:57-70 (DetailRow: a muted leading icon and a wrapping value) for the lines of a hit's preview.

namespace Barbican.Core.Search;

/// <summary>One detail line of a search hit's preview.</summary>
/// <param name="Icon">The line-icon key (mail, clock, pin, people, phone, building, folder, drive, text).</param>
/// <param name="Text">The value.</param>
public sealed record SearchDetail(string Icon, string Text);
