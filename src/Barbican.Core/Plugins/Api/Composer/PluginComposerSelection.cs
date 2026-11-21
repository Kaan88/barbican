// Mirrors: lib/plugin-sandbox/composer-edit.ts:51-59 (readSelection) and host-api-composer.ts:64-66.
namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>The selected text of an open composer, as HTML and plain text.</summary>
public sealed record PluginComposerSelection(string Html, string Text, bool Empty);
