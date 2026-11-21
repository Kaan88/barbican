// Mirrors: lib/plugin-sandbox/host-api-composer.ts:34-45 (readContent: { text } or { html }, html sanitised, text as paragraphs).
namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>What an edit inserts: sanitised editor HTML plus its plain text. The plain-text composer uses <see cref="Text"/>.</summary>
public sealed record PluginComposerContent(string Html, string Text);
