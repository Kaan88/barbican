// Mirrors: Bulwark lib/plugin-hook-catalog.ts:202 (onProvideRecipientSuggestions: RecipientSuggestion). Owner con-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A recipient suggestion a plugin may add or change.</summary>
public sealed record ContactSuggestion(string Name, string Email);
