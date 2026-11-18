// Mirrors: Bulwark lib/plugin-hook-catalog.ts:241 { keywords: KeywordView[] } (onKeywordChange); D-M6-A06. Owner set-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The reminder keywords (the words that make the composer ask about a missing attachment) after they changed.</summary>
public sealed record KeywordChangePayload(IReadOnlyList<string> Keywords);
