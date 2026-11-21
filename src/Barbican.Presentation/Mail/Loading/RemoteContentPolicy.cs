// Mirrors: Bulwark stores/settings-store.ts:444 (ExternalContentPolicy), :1118 (default 'ask'), settings/privacy-settings.tsx:104-116
// (Always ask / Always block / Always allow); components/email/email-viewer.tsx:3129-3146 (shouldBlockExternal). Owner reader, I-209.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>What the <c>externalContentPolicy</c> setting says about pictures and other content from the web in a message.</summary>
public enum RemoteContentPolicy
{
    /// <summary>Block, until the user loads this message or trusts its sender (the default).</summary>
    Ask,

    /// <summary>Block unless the sender is trusted; one message cannot be loaded on its own (no "Load images").</summary>
    Block,

    /// <summary>Never block.</summary>
    Allow,
}
