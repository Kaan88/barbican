// Mirrors: Rust src/rte/spell.rs:49-120 (sys::Checker: check skips CORRECTIVE_ACTION_DELETE, suggest returns at most 6 and not the word itself, add, ignore).
// Windows spellcheck.h ISpellChecker. Apartment-threaded COM: use it on the thread that created it (the UI thread).

using System.Runtime.InteropServices;
using Barbican.Core.Editor.Spell;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>One language's <c>ISpellChecker</c> behind <see cref="Barbican.Core.Editor.Spell.ISpellChecker"/>.</summary>
internal sealed unsafe class WindowsSpellChecker : Barbican.Core.Editor.Spell.ISpellChecker
{
    private const int MaxSuggestions = 6;
    private const int CorrectiveActionDelete = 3;
    private const int SOk = 0;

    private readonly ISpellCheckerCom _checker;

    public WindowsSpellChecker(ISpellCheckerCom checker) => _checker = checker;

    public IReadOnlyList<SpellRange> Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new List<SpellRange>();
        if (text.Length == 0 || _checker.Check(text, out var errors) != SOk || errors is null)
        {
            return output;
        }

        while (errors.Next(out var e) == SOk && e is not null)
        {
            // CORRECTIVE_ACTION_DELETE marks repeated words: not a spelling mistake
            if (e.GetCorrectiveAction(out var action) == SOk && action == CorrectiveActionDelete)
            {
                continue;
            }

            if (e.GetStartIndex(out var start) == SOk && e.GetLength(out var length) == SOk && start <= int.MaxValue && length <= int.MaxValue)
            {
                output.Add(new SpellRange((int)start, (int)length));
            }
        }

        return output;
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var output = new List<string>();
        if (word.Length == 0 || _checker.Suggest(word, out var en) != SOk || en is null)
        {
            return output;
        }

        while (output.Count < MaxSuggestions)
        {
            nint item = 0;
            uint got = 0;
            if (en.Next(1, &item, &got) != SOk || got == 0 || item == 0)
            {
                break;
            }

            var s = Marshal.PtrToStringUni(item) ?? "";
            SpellingNative.CoTaskMemFree(item);
            if (s.Length > 0 && !string.Equals(s, word, StringComparison.Ordinal))
            {
                output.Add(s);
            }
        }

        return output;
    }

    public void Add(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        _ = _checker.Add(word);
    }

    public void Ignore(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        _ = _checker.Ignore(word);
    }
}
