// Mirrors: Bulwark onEmailListItemRender (lib/plugin-hook-catalog.ts:73). Owner mailcore.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A visible row and the badges plugins add next to its subject (transform).</summary>
public sealed record ListItemRenderPayload(Barbican.Core.Mail.Model.EmailSummary Email, IReadOnlyList<string> Badges);
