// Mirrors: Rust mailview.rs:326 on_ipc (tag rust-final) and the message list of
//   src/Barbican.App/Assets/mail/shell.js. Bulwark: none (a web client has no host bridge).
namespace Barbican.Core.Mail.Body;

/// <summary>The page → host messages the body shell may send; anything else is dropped.</summary>
public enum ShellMessageKind
{
    /// <summary><c>shell</c>: the shell document is up; queued commands may be sent.</summary>
    Shell,

    /// <summary><c>ready:N</c>: frame N is laid out (and dark-converted) and may be revealed.</summary>
    Ready,

    /// <summary><c>dirty</c>: the visible page changed (scroll, resize, load, selection).</summary>
    Dirty,

    /// <summary><c>key:FLAGS|KEY</c>: a shortcut pressed while the page had focus (parsed further by the key router).</summary>
    Key,

    /// <summary><c>ctx:X,Y</c>: a right-click at page coordinates; the host opens its own menu.</summary>
    Context,

    /// <summary><c>link:URL</c>: a link click the shell intercepted; only http, https and mailto pass.</summary>
    Link,

    /// <summary><c>sel:TEXT</c>: the selected text changed (debounced by the shell; capped, controls dropped); feeds onTextSelectionChange.</summary>
    Selection,
}
