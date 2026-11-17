// Mirrors: Rust src/rte/spell.rs (tag rust-final), platform/hunspell.rs; Windows ISpellChecker (spellcheck.h), Hunspell dictionaries via WeCantSpell.Hunspell. Contract from the M3 plan (stream editor-core).

namespace Barbican.Core.Editor.Spell;

/// <summary>One language's checker: Windows <c>ISpellChecker</c> (Platform.Windows) or Hunspell (Platform.Linux, M9). Results are cached by the engine per paragraph text.</summary>
public interface ISpellChecker
{
    IReadOnlyList<SpellRange> Check(string text);

    IReadOnlyList<string> Suggest(string word);

    void Add(string word);

    void Ignore(string word);
}
