// Mirrors: Bulwark lib/plugin-hook-catalog.ts:268-:269, :272 (TemplateView). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A template as plugins see it (onTemplateCreate, onTemplateUpdate, onTemplateApply).</summary>
public sealed record TemplateAdminViewPayload(string Id, string Name, string Subject, string Body, bool IsHtml, string Category, bool IsFavorite);
