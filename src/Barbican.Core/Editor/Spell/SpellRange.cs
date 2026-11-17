// Mirrors: Rust src/rte/spell.rs (tag rust-final), platform/hunspell.rs; Windows ISpellChecker (spellcheck.h), Hunspell dictionaries via WeCantSpell.Hunspell. Contract from the M3 plan (stream editor-core).

using System.Runtime.InteropServices;

namespace Barbican.Core.Editor.Spell;

/// <summary>A misspelt range of a text in UTF-16 code units (what <c>ISpellChecker::Check</c> reports).</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SpellRange(int Start, int Length)
{
    public int End => Start + Length;
}
