// Mirrors: Bulwark lib/plugin-sandbox/host-api-compose.ts:79-118 doComposeOpen ('opened' in place, or 'navigating' when the mail page has to open first) and :121-142 doComposeReply.
namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>What the shell did with a compose request.</summary>
public enum PluginComposeOpenResult
{
    /// <summary>The composer is open and filled in; the user decides whether to send.</summary>
    Opened,

    /// <summary>The app went to the mail view, which opens the composer.</summary>
    Navigating,

    /// <summary>Nothing opened (the user kept the draft already being written, or the message is not the open one).</summary>
    Refused,
}
