// Mirrors: Bulwark lib/plugin-hook-catalog.ts:271 (onTemplatesImport { count }). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>Templates were imported.</summary>
public sealed record TemplateAdminImportPayload(int Count);
