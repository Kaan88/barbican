// Mirrors: Bulwark lib/plugin-hook-catalog.ts:202 (onProvideRecipientSuggestions: RecipientSuggestion[], { query }; a transform: handlers see the previous result). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The suggestions for a typed text, and the text.</summary>
public sealed record ContactSuggestionsPayload(string Query, IReadOnlyList<ContactSuggestion> Suggestions);
