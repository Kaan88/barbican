// Mirrors: Bulwark lib/plugin-hook-catalog.ts:86 onBeforeExternalLink (ExternalLinkContext: href, source); spec/plugins/hooks.csv. Owner reader.

namespace Barbican.Core.Mail.Reader.Hooks;

/// <summary>A link about to open outside the app.</summary>
/// <param name="Href">The absolute URL.</param>
/// <param name="Source">Where it came from: "mail" (a link in a message), "unsubscribe".</param>
public sealed record ExternalLinkContext(string Href, string Source);
