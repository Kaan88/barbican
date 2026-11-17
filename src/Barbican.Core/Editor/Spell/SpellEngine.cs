// Mirrors: Rust src/rte/spell.rs:127-183 (with_checker, misspelled, suggestions, add, ignore, reload; results cached per text, cache cleared above 2000 entries).
// Windows ISpellChecker (spellcheck.h) or Hunspell sits behind ISpellCheckerFactory. No Stalwart counterpart.

namespace Barbican.Core.Editor.Spell;

/// <summary>
/// The cached spelling service over an <see cref="ISpellCheckerFactory"/>: the checker follows the language setting (rebuilt and the cache dropped when the tag
/// changes), results are cached per paragraph text (so typing re-checks only the changed paragraph), URLs, addresses and words with digits are skipped.
/// UI thread only: the Windows checker is an apartment-threaded COM object.
/// </summary>
public sealed class SpellEngine : ISpellEngine
{
    /// <summary>Entries kept before the cache starts over (Rust used 2000).</summary>
    public const int CacheLimit = 2000;

    private readonly ISpellCheckerFactory _factory;
    private readonly Func<SpellSettings> _settings;
    private readonly Dictionary<string, IReadOnlyList<SpellRange>> _cache = new(StringComparer.Ordinal);
    private string? _userLocale;
    private string? _tag;
    private ISpellChecker? _checker;

    public SpellEngine(ISpellCheckerFactory factory, Func<SpellSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(settings);
        _factory = factory;
        _settings = settings;
    }

    public bool IsAvailable => Current() is not null;

    /// <summary>Entries in the cache (for tests and diagnostics).</summary>
    public int CachedTexts => _cache.Count;

    public IReadOnlyList<SpellRange> Misspelled(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text) || Current() is not { } checker)
        {
            return [];
        }

        if (_cache.TryGetValue(text, out var hit))
        {
            return hit;
        }

        var found = SpellText.ToTextRanges(text, checker.Check(text))
            .Where(r => !SpellText.SkipWord(SpellText.Token(text, r.Start, r.End)))
            .Select(r => new SpellRange(r.Start, r.End - r.Start))
            .ToList();
        if (_cache.Count > CacheLimit)
        {
            _cache.Clear();
        }

        _cache[text] = found;
        return found;
    }

    public IReadOnlyList<string> Suggestions(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return Current()?.Suggest(word) ?? [];
    }

    public void Add(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (Current() is { } c)
        {
            c.Add(word);
            _cache.Clear();
        }
    }

    public void Ignore(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (Current() is { } c)
        {
            c.Ignore(word);
            _cache.Clear();
        }
    }

    /// <summary>The setting changed: drop the checker and the cache.</summary>
    public void Reload()
    {
        _tag = null;
        _checker = null;
        _userLocale = null;
        _cache.Clear();
    }

    /// <summary>The checker for the current settings, null when spelling is off or the machine has no dictionary for the language.</summary>
    private ISpellChecker? Current()
    {
        var s = _settings();
        if (!s.Enabled)
        {
            return null;
        }

        var tag = SpellText.LanguageTag(s.Language, _userLocale ??= _factory.UserLocale);
        if (_tag is null || !string.Equals(_tag, tag, StringComparison.Ordinal))
        {
            _cache.Clear();
            _tag = tag;
            _checker = _factory.Create(tag);
        }

        return _checker;
    }
}
