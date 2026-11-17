// Mirrors: Rust src/rte/spell.rs (tag rust-final), platform/hunspell.rs; Windows ISpellChecker (spellcheck.h), Hunspell dictionaries via WeCantSpell.Hunspell. Contract from the M3 plan (stream editor-core).

namespace Barbican.Core.Editor.Spell;

/// <summary>The default factory: no dictionary, so the composer shows no spelling marks (Linux until M9, tests).</summary>
public sealed class NoSpellCheckerFactory : ISpellCheckerFactory
{
    public string UserLocale => "";

    public ISpellChecker? Create(string languageTag) => null;
}
