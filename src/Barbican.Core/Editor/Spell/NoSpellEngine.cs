// Mirrors: see ISpellEngine. Default registration from the M3 plan section 4.2; owner editor-core replaces it with the cached engine over ISpellCheckerFactory.

namespace Barbican.Core.Editor.Spell;

/// <summary>The default: no spelling.</summary>
public sealed class NoSpellEngine : ISpellEngine
{
    public bool IsAvailable => false;

    public IReadOnlyList<SpellRange> Misspelled(string text) => [];

    public IReadOnlyList<string> Suggestions(string word) => [];

    public void Add(string word)
    {
    }

    public void Ignore(string word)
    {
    }

    public void Reload()
    {
    }
}
