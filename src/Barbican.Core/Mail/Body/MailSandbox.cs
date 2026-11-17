// Mirrors: Rust mailview.rs:140 sandbox() (tag rust-final), with one deliberate change: no
//   allow-popups / allow-popups-to-escape-sandbox (links are routed by the shell itself now,
//   docs/adr/0005 decision 4). HTML Living Standard §4.8.5 (iframe sandbox keywords).
// Bulwark: components/email/email-viewer.tsx:6548 and thread-conversation-view.tsx:1262
//   ("allow-same-origin allow-popups allow-popups-to-escape-sandbox": a browser tab opens links as
//   popups; the desktop shell routes them to the host instead, so popups are not needed).
// Stalwart: none.
namespace Barbican.Core.Mail.Body;

/// <summary>
/// The <c>sandbox</c> attribute of a message frame. Never <c>allow-top-navigation</c> (any variant),
/// <c>allow-popups</c>, <c>allow-forms</c> or <c>allow-downloads</c>: a message cannot navigate the
/// shell, open a window, submit a form or start a download. Links reach the app only through the
/// shell's click handler (<c>link:URL</c>) and the host's scheme check (<see cref="ShellMessage"/>).
/// <para><c>allow-same-origin</c> stays: the shell reads the frame document to see when it is laid out,
/// to attach the key, context-menu and link handlers and to check the dark pass. Mail scripts still
/// never run (no <c>allow-scripts</c> unless the page carries our own hashed script, and the CSP
/// allows only that hash). shell.js applies the same allow-list again (<c>SANDBOX_OK</c>).</para>
/// </summary>
public static class MailSandbox
{
    public const string SameOrigin = "allow-same-origin";
    public const string Scripts = "allow-scripts";
    public const string Modals = "allow-modals";

    /// <summary>Every keyword a frame may ever get (shell.js holds the same list).</summary>
    public static IReadOnlyList<string> Allowed { get; } = [SameOrigin, Scripts, Modals];

    /// <summary>The sandbox for a frame; <paramref name="script"/> = the page carries our own hashed script.</summary>
    public static string For(bool script) => script
        ? $"{SameOrigin} {Modals} {Scripts}"
        : $"{SameOrigin} {Modals}";
}
