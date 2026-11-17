// Mirrors: Rust shortcuts.rs:15-27 (Slint key texts, i-slint-common key_codes.rs) and :30 GO_TO_SEQUENCE. Stub from stage F of M2; owner menus.
// The C# key router takes KeyChord values; the Rust tests are ported against key *texts*, so the same private-use code points name the
// non-printing keys here and the menus stream maps KeyChord to these texts (or ports the table to chords and updates the tests with a D- entry).

namespace Barbican.Presentation.Input;

/// <summary>The key texts the resolver understands for non-printing keys.</summary>
public static class ShortcutKeys
{
    public const string Up = "";
    public const string Down = "";
    public const string Left = "";
    public const string Right = "";
    public const string Home = "";
    public const string End = "";
    public const string Escape = "\u001b";
    public const string Delete = "\u007f";
    public const string Backspace = "\b";
    public const string F2 = "";
    public const string F5 = "";
    public const string F9 = "";
    public const string F10 = "";

    /// <summary>How long a pending g waits for its second key (Bulwark GO_TO_SEQUENCE_MS).</summary>
    public static TimeSpan GoToSequence { get; } = TimeSpan.FromMilliseconds(1500);
}
