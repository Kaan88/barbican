// Mirrors: Rust mailview.rs:295 forward_key (tag rust-final: the parsed key is replayed in the native window, so the app's shortcuts work
// with the mail view focused). Here it becomes the chord the KeyRouter reads (KeySource.WebView). Owner body.

using Barbican.Core.Mail.Body;
using Barbican.Presentation.Input;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Turns a key the mail view forwarded into a key chord.</summary>
public static class ForwardedKeyChords
{
    public static KeyChord ToChord(ForwardedKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var name = key.Text switch
        {
            ShellMessageParser.Escape => "Escape",
            ShellMessageParser.Delete => "Delete",
            ShellMessageParser.F5 => "F5",
            ShellMessageParser.F9 => "F9",
            var t => t,
        };
        return new KeyChord(name, key.Modifiers.HasFlag(KeyModifiers.Control), key.Modifiers.HasFlag(KeyModifiers.Shift), key.Modifiers.HasFlag(KeyModifiers.Alt));
    }
}
