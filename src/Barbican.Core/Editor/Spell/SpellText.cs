// Mirrors: Rust src/rte/spell.rs (tag rust-final), platform/hunspell.rs; Windows ISpellChecker (spellcheck.h), Hunspell dictionaries via WeCantSpell.Hunspell. Contract from the M3 plan (stream editor-core).

using Barbican.Core.Common;

namespace Barbican.Core.Editor.Spell;

/// <summary>The pure helpers around a checker (port of Rust <c>language_tag</c>, <c>utf16_to_bytes</c>, <c>skip_word</c>, <c>token</c>). Stub until stream editor-core lands.</summary>
public static class SpellText
{
    /// <summary>Language tag for the <c>writingLanguage</c> setting: en, de, fr, es, tr map to en-US, de-DE, fr-FR, es-ES, tr-TR; "auto" or "" is the user locale (en-US when unknown); anything else as is.</summary>
    public static string LanguageTag(string pref, string userLocale) => throw new PortPendingException("SpellText.LanguageTag");

    /// <summary>The checker's (start, length) ranges as valid (start, end) index ranges of <paramref name="text"/>: UTF-16 already, so this only drops empty or out-of-range ranges (Rust converted to UTF-8 byte offsets, D-240).</summary>
    public static IReadOnlyList<(int Start, int End)> ToTextRanges(string text, IReadOnlyList<SpellRange> ranges) => throw new PortPendingException("SpellText.ToTextRanges");

    /// <summary>Words the checker must not flag: URLs, e-mail addresses, words with digits, single characters.</summary>
    public static bool SkipWord(string word) => throw new PortPendingException("SpellText.SkipWord");

    /// <summary>The whitespace-delimited token around the range [start, end) of <paramref name="text"/>.</summary>
    public static string Token(string text, int start, int end) => throw new PortPendingException("SpellText.Token");
}
