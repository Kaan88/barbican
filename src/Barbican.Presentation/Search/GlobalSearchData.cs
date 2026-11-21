// Mirrors: Bulwark lib/global-search/providers/{contacts,calendar,files}.ts (the data the local pass reads: the contact, calendar and files stores).

using System.Text.Json.Nodes;
using Barbican.Core.Files;

namespace Barbican.Presentation.Search;

/// <summary>What the active login holds of contacts, calendar events and files, for the palette's search over them.</summary>
public sealed record GlobalSearchData
{
    /// <summary>Nothing loaded (no login, or none of the three capabilities).</summary>
    public static GlobalSearchData Empty { get; } = new();

    public IReadOnlyList<JsonNode> Cards { get; init; } = [];

    public IReadOnlyList<JsonNode> Books { get; init; } = [];

    public IReadOnlyList<JsonNode> Events { get; init; } = [];

    public IReadOnlyList<JsonNode> Calendars { get; init; } = [];

    public IReadOnlyList<FileEntry> Files { get; init; } = [];
}
