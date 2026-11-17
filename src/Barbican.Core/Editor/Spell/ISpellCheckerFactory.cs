// Mirrors: Rust src/rte/spell.rs (tag rust-final), platform/hunspell.rs; Windows ISpellChecker (spellcheck.h), Hunspell dictionaries via WeCantSpell.Hunspell. Contract from the M3 plan (stream editor-core).

namespace Barbican.Core.Editor.Spell;

/// <summary>Creates the checker for a language tag (<see cref="SpellText.LanguageTag"/>); null when the OS has no dictionary for it. The default registration returns null (no spelling).</summary>
public interface ISpellCheckerFactory
{
    /// <summary>The user's locale tag (Windows user locale, or the LANG of the session), "" when unknown.</summary>
    string UserLocale { get; }

    ISpellChecker? Create(string languageTag);
}
