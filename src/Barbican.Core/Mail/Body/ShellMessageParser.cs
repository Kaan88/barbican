// Mirrors: Rust mailview.rs:278 parse_key and :326 on_ipc (tag rust-final): the key a mail view caught, as "flags|key" (c, s, a), with
// only Escape, Delete, F5, F9 and one-character keys accepted. The syntax gate is ShellMessage (F1); this is the meaning. The Slint key
// characters become the same code points (Escape U+001B, Delete U+007F, F5 U+F704, F9 U+F708). Bulwark: none (no host bridge). Owner body.

namespace Barbican.Core.Mail.Body;

/// <summary>Reads the messages the mail view posts to the native side.</summary>
public static class ShellMessageParser
{
    /// <summary>Escape as a key character (Slint <c>Key::Escape</c>).</summary>
    public const string Escape = "\u001b";

    /// <summary>Delete as a key character (Slint <c>Key::Delete</c>).</summary>
    public const string Delete = "\u007f";

    /// <summary>F5 as a key character (Slint <c>Key::F5</c>).</summary>
    public const string F5 = "";

    /// <summary>F9 as a key character (Slint <c>Key::F9</c>).</summary>
    public const string F9 = "";

    /// <summary>Parses <c>flags|key</c> (flags among c, s, a); anything the page may not forward gives null.</summary>
    public static ForwardedKey? ParseKey(string spec)
    {
        if (string.IsNullOrEmpty(spec))
        {
            return null;
        }

        var bar = spec.IndexOf('|', StringComparison.Ordinal);
        if (bar < 0)
        {
            return null;
        }

        var flags = spec.AsSpan(0, bar);
        var key = spec[(bar + 1)..];
        var text = key switch
        {
            "Escape" => Escape,
            "Delete" => Delete,
            "F5" => F5,
            "F9" => F9,
            _ when IsOneCharacter(key) => key,
            _ => null,
        };
        if (text is null)
        {
            return null;
        }

        var mods = KeyModifiers.None;
        if (flags.Contains('c'))
        {
            mods |= KeyModifiers.Control;
        }

        if (flags.Contains('s'))
        {
            mods |= KeyModifiers.Shift;
        }

        if (flags.Contains('a'))
        {
            mods |= KeyModifiers.Alt;
        }

        return new ForwardedKey(mods, text);
    }

    // Rust k.chars().count() == 1: one Unicode scalar (a surrogate pair counts as one)
    private static bool IsOneCharacter(string key) =>
        key.Length == 1 ? !char.IsSurrogate(key[0]) : key.Length == 2 && char.IsSurrogatePair(key[0], key[1]);
}
