// Mirrors: lib/plugin-sandbox/host-api-composer.ts:60-66 (composer.getBody result { html, text }).
namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>The user's own text of an open composer, above the signature and the quoted original.</summary>
public sealed record PluginComposerBody(string Html, string Text);
