// Mirrors: Rust src/rte/model.rs (tag rust-final); Bulwark components/email/email-composer.tsx (TipTap schema).
// No RFC or Stalwart counterpart: the composer document model is client-side only.

using System.Globalization;

namespace Barbican.Core.Editor;

/// <summary>
/// Caret steps over block text. Rust stepped over <c>char</c>s (code points); here a step is one
/// extended grapheme cluster (UAX #29), so the caret never lands inside an emoji ZWJ sequence or
/// between a letter and its combining mark. Offsets are UTF-16 code units.
/// </summary>
public static class TextOffsets
{
    public static int NextChar(string s, int o)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (o >= s.Length)
        {
            return s.Length;
        }

        return o + StringInfo.GetNextTextElementLength(s, o);
    }

    public static int PrevChar(string s, int o)
    {
        ArgumentNullException.ThrowIfNull(s);
        var prev = 0;
        var at = 0;
        while (at < o && at < s.Length)
        {
            prev = at;
            at += StringInfo.GetNextTextElementLength(s, at);
        }

        return prev;
    }

    /// <summary>Clamps to [0, length] and moves back off the low half of a surrogate pair.</summary>
    public static int Floor(string s, int o)
    {
        ArgumentNullException.ThrowIfNull(s);
        o = Math.Clamp(o, 0, s.Length);
        if (o > 0 && o < s.Length && char.IsLowSurrogate(s[o]) && char.IsHighSurrogate(s[o - 1]))
        {
            o--;
        }

        return o;
    }
}
