// Mirrors: Rust src/rte/spell.rs (tag rust-final) language_tag, utf16_to_bytes, skip_word, token; Windows ISpellChecker (spellcheck.h) reports UTF-16 (start, length).
// Decision D-240 (spec/decisions.md): offsets stay UTF-16, so utf16_to_bytes becomes a bounds filter.

namespace Barbican.Core.Editor.Spell;

/// <summary>The pure helpers around a checker.</summary>
public static class SpellText
{
    /// <summary>Language tag for the <c>writingLanguage</c> setting: en, de, fr, es, tr map to en-US, de-DE, fr-FR, es-ES, tr-TR; "auto" or "" is the user locale (en-US when unknown); anything else as is.</summary>
    public static string LanguageTag(string pref, string userLocale)
    {
        ArgumentNullException.ThrowIfNull(pref);
        ArgumentNullException.ThrowIfNull(userLocale);
        return pref switch
        {
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "tr" => "tr-TR",
            "" or "auto" => userLocale.Length == 0 ? "en-US" : userLocale,
            _ => pref,
        };
    }

    /// <summary>The checker's (start, length) ranges as valid (start, end) index ranges of <paramref name="text"/>: UTF-16 already, so this only drops empty or out-of-range ranges (Rust converted to UTF-8 byte offsets, D-240).</summary>
    public static IReadOnlyList<(int Start, int End)> ToTextRanges(string text, IReadOnlyList<SpellRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ranges);
        var output = new List<(int, int)>(ranges.Count);
        foreach (var r in ranges)
        {
            var end = (long)r.Start + r.Length;
            if (r.Start >= 0 && r.Length > 0 && end <= text.Length)
            {
                output.Add((r.Start, (int)end));
            }
        }

        return output;
    }

    /// <summary>Words the checker must not flag: URLs, e-mail addresses, words with digits, single characters.</summary>
    public static bool SkipWord(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return word.Contains("://", StringComparison.Ordinal)
            || word.Contains('@', StringComparison.Ordinal)
            || word.StartsWith("www.", StringComparison.Ordinal)
            || word.Any(char.IsAsciiDigit)
            || word.EnumerateRunes().Take(2).Count() < 2;
    }

    /// <summary>The whitespace-delimited token around the range [start, end) of <paramref name="text"/>.</summary>
    public static string Token(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);
        var a = start;
        while (a > 0 && !char.IsWhiteSpace(text[a - 1]))
        {
            a--;
        }

        var b = end;
        while (b < text.Length && !char.IsWhiteSpace(text[b]))
        {
            b++;
        }

        return text[a..b];
    }
}
