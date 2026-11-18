// Mirrors: Bulwark lib/plugin-hook-catalog.ts:273 (onTemplateRender: { subject, html, text } and { templateId }). Owner srv-data.

namespace Barbican.Core.Plugins.Payloads;

/// <summary>The text of a template about to be inserted into the composer; a transform handler returns it changed (the Html is the body of a rich template, the Text of a plain one).</summary>
public sealed record TemplateAdminRenderPayload(string TemplateId, string Subject, string Html, string Text);
