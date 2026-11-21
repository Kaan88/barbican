// Mirrors: Bulwark stores/settings-store.ts:1118 (externalContentPolicy defaults to 'ask'), components/email/email-viewer.tsx:3129-3146
// (shouldBlockExternal: a trusted sender loads in every mode; 'allow' never blocks; 'ask' blocks until the message is allowed; 'block'
// has no per-message load), :6359 (the Load button exists only in ask mode), :6370 (no trust button without a sender). Owner reader, I-209.

using Barbican.Core.Storage;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The pure rules of remote content: the setting, whether a message may load it, and the notice to show when it may not.</summary>
public static class RemoteContentRules
{
    /// <summary>The Bulwark pref key (spec S-externalContentPolicy).</summary>
    public const string SettingKey = "externalContentPolicy";

    /// <summary>The setting in effect; an absent or unknown value is <see cref="RemoteContentPolicy.Ask"/>, the same first run Bulwark has.</summary>
    public static RemoteContentPolicy PolicyOf(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return prefs.Str(SettingKey, "ask") switch
        {
            "allow" => RemoteContentPolicy.Allow,
            "block" => RemoteContentPolicy.Block,
            _ => RemoteContentPolicy.Ask,
        };
    }

    /// <summary>
    /// Whether a message may load remote content: a trusted sender always may, 'allow' always may, and in ask mode the user's answer for
    /// this one message counts. A body plugin's block (<c>BlockRemote</c>) is applied by the caller and wins over every answer here.
    /// </summary>
    /// <param name="policy">The setting in effect.</param>
    /// <param name="trustedSender">The sender is in <c>TrustedSenders</c>.</param>
    /// <param name="allowedForMessage">The user chose "Load images" for this message (ask mode).</param>
    public static bool MayLoad(RemoteContentPolicy policy, bool trustedSender, bool allowedForMessage) =>
        trustedSender || policy == RemoteContentPolicy.Allow || (policy == RemoteContentPolicy.Ask && allowedForMessage);

    /// <summary>
    /// The notice above the body, or null when there is nothing to say: no remote content, content that may load, or a body plugin that
    /// asked for it to stay off (its own banner explains, Bulwark email-viewer.tsx:6342).
    /// </summary>
    /// <param name="hasRemoteContent">The HTML refers to pictures, backgrounds or fonts from the web.</param>
    /// <param name="allowed">This page was built with remote content allowed.</param>
    /// <param name="blockRemote">A body plugin asked that it stay blocked whatever the user allows.</param>
    /// <param name="policy">The setting in effect.</param>
    /// <param name="trustedSender">The sender is trusted already.</param>
    /// <param name="hasSender">The message names a sender address that can be trusted.</param>
    public static RemoteContentNotice? Notice(bool hasRemoteContent, bool allowed, bool blockRemote, RemoteContentPolicy policy, bool trustedSender, bool hasSender)
    {
        if (!hasRemoteContent || allowed || blockRemote || policy == RemoteContentPolicy.Allow)
        {
            return null;
        }

        List<(string ActionId, string Label)> actions = [];
        if (policy == RemoteContentPolicy.Ask)
        {
            actions.Add((RemoteContentNotice.Load, "Load images"));
        }

        if (!trustedSender && hasSender)
        {
            actions.Add((RemoteContentNotice.Trust, "Always trust this sender"));
        }

        return new RemoteContentNotice(RemoteContentNotice.Heading, RemoteContentNotice.BlockedMessage, actions);
    }
}
