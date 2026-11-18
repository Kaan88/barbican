// Contract from the M5 plan section 4.1 (G-012, L-034); owner con-data.

using System.Text.Json.Nodes;

namespace Barbican.Core.Contacts;

/// <summary>What <c>VCardParser.ParseChecked</c> read: the cards, and what the limits left out.</summary>
public sealed record VCardParseResult(IReadOnlyList<JsonNode> Cards, int SkippedCards, int PhotosDropped);
