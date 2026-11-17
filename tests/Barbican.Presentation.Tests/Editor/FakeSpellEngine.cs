using Barbican.Core.Editor.Spell;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>A dictionary of three wrong words with suggestions, and a record of what the user added or ignored.</summary>
internal sealed class FakeSpellEngine : ISpellEngine
{
    private static readonly Dictionary<string, string[]> Wrong = new(StringComparer.Ordinal)
    {
        ["speling"] = ["spelling", "spieling", "spewing", "spelunking", "spelling's", "extra"],
        ["teh"] = ["the", "tea"],
        ["xyzzy"] = [],
    };

    public List<string> Added { get; } = [];

    public List<string> Ignored { get; } = [];

    public bool IsAvailable => true;

    public IReadOnlyList<SpellRange> Misspelled(string text)
    {
        var result = new List<SpellRange>();
        var i = 0;
        foreach (var word in text.Split(' '))
        {
            if (Wrong.ContainsKey(word) && !Added.Contains(word) && !Ignored.Contains(word))
            {
                result.Add(new SpellRange(i, word.Length));
            }

            i += word.Length + 1;
        }

        return result;
    }

    public IReadOnlyList<string> Suggestions(string word) => Wrong.TryGetValue(word, out var s) ? s : [];

    public void Add(string word) => Added.Add(word);

    public void Ignore(string word) => Ignored.Add(word);

    public void Reload()
    {
    }
}
