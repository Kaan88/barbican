// Mirrors: Rust src/rte/spell.rs:127-183 (with_checker, misspelled, suggestions, add, ignore, reload; results cached per text); Bulwark settings writingLanguage. Contract from the M3 plan section 4.2; owner editor-core implements, editor-view consumes.

namespace Barbican.Core.Editor.Spell;

/// <summary>
/// The spelling service the editor view asks per paragraph: misspelt ranges of a block's text (cached per text, so only changed paragraphs are checked again), suggestions for a word, add to dictionary, ignore. The language follows
/// the <c>writingLanguage</c> setting. Unavailable (no dictionary, Linux before M9) means no marks and no menu entries.
/// </summary>
public interface ISpellEngine
{
    bool IsAvailable { get; }

    /// <summary>Misspelt ranges of <paramref name="text"/> in UTF-16 indices, URLs, addresses and words with digits skipped (<see cref="SpellText.SkipWord"/>).</summary>
    IReadOnlyList<SpellRange> Misspelled(string text);

    IReadOnlyList<string> Suggestions(string word);

    void Add(string word);

    void Ignore(string word);

    /// <summary>The setting changed: drop the checker and the cache.</summary>
    void Reload();
}
