// Mirrors: Rust mailview.rs:140 sandbox() (tag rust-final), with deliberate changes: no allow-popups / allow-popups-to-escape-sandbox
//   (links are routed by the shell itself, docs/adr/0005 decision 4), and never allow-scripts (Fable review 1 F-03: allow-scripts together
//   with allow-same-origin lets framed content reach the shell, the host bridge and the other frames; the dark pass and the text sizing run
//   from the shell against the frame document instead). HTML Living Standard §4.8.5 (iframe sandbox keywords; scripts also gate the
//   "automatic features" such as meta refresh).
// Bulwark: components/email/email-viewer.tsx:6548 and thread-conversation-view.tsx:1262
//   ("allow-same-origin allow-popups allow-popups-to-escape-sandbox": a browser tab opens links as
//   popups; the desktop shell routes them to the host instead, so popups are not needed).
// Stalwart: none.
namespace Barbican.Core.Mail.Body;

/// <summary>
/// The <c>sandbox</c> attribute of a message frame: always <see cref="Frame"/>. Never <c>allow-scripts</c>, <c>allow-top-navigation</c>
/// (any variant), <c>allow-popups</c>, <c>allow-forms</c> or <c>allow-downloads</c>: a message cannot run code, navigate the shell, open a
/// window, submit a form or start a download. Links reach the app only through the shell's click handler (<c>link:URL</c>) and the host's
/// scheme check (<see cref="ShellMessage"/>).
/// <para><c>allow-same-origin</c> stays: the shell reads the frame document to see when it is laid out, to attach the key, context-menu and
/// link handlers, and to run the dark pass and the text sizing on it. Without <c>allow-scripts</c> nothing in the frame can use that origin.
/// shell.js applies the same allow-list again (<c>SANDBOX_OK</c>). Fable F-03 point 4: the shell's key messages are safe only because a
/// mail can never post; keep it that way.</para>
/// </summary>
public static class MailSandbox
{
    public const string SameOrigin = "allow-same-origin";
    public const string Modals = "allow-modals";

    /// <summary>The sandbox of every message frame.</summary>
    public const string Frame = SameOrigin + " " + Modals;

    /// <summary>Every keyword a frame may ever get (shell.js holds the same list).</summary>
    public static IReadOnlyList<string> Allowed { get; } = [SameOrigin, Modals];
}
