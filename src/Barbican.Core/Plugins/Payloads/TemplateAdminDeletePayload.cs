// Mirrors: Bulwark lib/plugin-hook-catalog.ts:270 (onTemplateDelete { templateId }). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>A template was deleted.</summary>
public sealed record TemplateAdminDeletePayload(string TemplateId);
