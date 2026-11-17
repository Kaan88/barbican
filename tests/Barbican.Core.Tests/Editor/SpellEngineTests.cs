#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using System.Text.Json.Nodes;
using Barbican.Core.Editor;
using Barbican.Core.Editor.Spell;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Core.Tests.Editor;

/// <summary>The cached spell engine over a fake checker (no OS dictionary needed), the pure helpers and the DI wiring.</summary>
public sealed class SpellEngineTests
{
    private sealed class FakeChecker(string tag) : ISpellChecker
    {
        public int Checks { get; private set; }

        public List<string> Added { get; } = [];

        public List<string> Ignored { get; } = [];

        public string Tag { get; } = tag;

        public IReadOnlyList<SpellRange> Check(string text)
        {
            Checks++;

            // every "wrng" is wrong, plus the URL and the word with a digit, which the engine must skip
            var found = new List<SpellRange>();
            foreach (var w in new[] { "wrng", "https://x.example/wrng", "abc1" })
            {
                for (var i = text.IndexOf(w, StringComparison.Ordinal); i >= 0; i = text.IndexOf(w, i + w.Length, StringComparison.Ordinal))
                {
                    found.Add(new SpellRange(i, w.Length));
                }
            }

            found.Add(new SpellRange(-1, 3)); // out of range: dropped
            return found;
        }

        public IReadOnlyList<string> Suggest(string word) => [word + "!"];

        public void Add(string word) => Added.Add(word);

        public void Ignore(string word) => Ignored.Add(word);
    }

    private sealed class FakeFactory(string userLocale = "en-GB", params string[] languages) : ISpellCheckerFactory
    {
        public List<FakeChecker> Created { get; } = [];

        public string UserLocale { get; } = userLocale;

        public ISpellChecker? Create(string languageTag)
        {
            if (languages.Length > 0 && !languages.Contains(languageTag, StringComparer.Ordinal))
            {
                return null;
            }

            var c = new FakeChecker(languageTag);
            Created.Add(c);
            return c;
        }
    }

    private static SpellEngine Engine(FakeFactory f, SpellSettings? settings = null) => new(f, () => settings ?? SpellSettings.Default);

    [Fact]
    public void An_unchanged_text_is_checked_once_and_returns_the_stored_result()
    {
        var f = new FakeFactory();
        var e = Engine(f);
        var a = e.Misspelled("a wrng here");
        var b = e.Misspelled("a wrng here");
        Assert.Same(a, b);
        Assert.Equal(1, f.Created[0].Checks);
        Assert.Equal([new SpellRange(2, 4)], a);
        _ = e.Misspelled("another wrng");
        Assert.Equal(2, f.Created[0].Checks);
        Assert.Equal(2, e.CachedTexts);
    }

    [Fact]
    public void Urls_and_words_with_digits_are_skipped_and_blank_text_is_never_checked()
    {
        var f = new FakeFactory();
        var e = Engine(f);
        Assert.Equal([new SpellRange(0, 4)], e.Misspelled("wrng https://x.example/wrng abc1"));
        Assert.Empty(e.Misspelled("   "));
        Assert.Empty(e.Misspelled(""));
        Assert.Equal(1, f.Created[0].Checks);
    }

    [Fact]
    public void The_cache_starts_over_above_its_limit()
    {
        var f = new FakeFactory();
        var e = Engine(f);
        for (var i = 0; i <= SpellEngine.CacheLimit + 5; i++)
        {
            _ = e.Misspelled("wrng " + i);
        }

        Assert.True(e.CachedTexts <= SpellEngine.CacheLimit + 1);
    }

    [Fact]
    public void The_language_setting_picks_the_checker_and_a_change_rebuilds_it_and_drops_the_cache()
    {
        var f = new FakeFactory("en-GB");
        var language = "auto";
        var e = new SpellEngine(f, () => new SpellSettings(Enabled: true, language));
        _ = e.Misspelled("wrng");
        Assert.Equal("en-GB", f.Created[^1].Tag);
        language = "de";
        _ = e.Misspelled("wrng");
        Assert.Equal("de-DE", f.Created[^1].Tag);
        Assert.Equal(2, f.Created.Count);
        language = "fr-CA";
        _ = e.Misspelled("wrng");
        Assert.Equal("fr-CA", f.Created[^1].Tag);
        Assert.Equal(1, e.CachedTexts);
    }

    [Fact]
    public void Disabled_or_no_dictionary_means_no_marks_and_not_available()
    {
        var f = new FakeFactory("en-GB", "en-GB");
        var off = Engine(f, new SpellSettings(Enabled: false, "auto"));
        Assert.False(off.IsAvailable);
        Assert.Empty(off.Misspelled("wrng"));
        Assert.Empty(f.Created);

        var none = Engine(f, new SpellSettings(Enabled: true, "tlh"));
        Assert.False(none.IsAvailable);
        Assert.Empty(none.Misspelled("wrng"));
        Assert.Empty(none.Suggestions("x"));
        none.Add("w");
        none.Ignore("w");

        Assert.True(Engine(f).IsAvailable);
    }

    [Fact]
    public void Suggestions_add_and_ignore_go_to_the_checker_and_clear_the_cache()
    {
        var f = new FakeFactory();
        var e = Engine(f);
        _ = e.Misspelled("wrng");
        Assert.Equal(["wrng!"], e.Suggestions("wrng"));
        e.Add("wrng");
        Assert.Equal(["wrng"], f.Created[0].Added);
        Assert.Equal(0, e.CachedTexts);
        _ = e.Misspelled("wrng");
        e.Ignore("wrng");
        Assert.Equal(["wrng"], f.Created[0].Ignored);
        Assert.Equal(0, e.CachedTexts);
    }

    [Fact]
    public void Reload_drops_the_checker_and_the_cache()
    {
        var f = new FakeFactory();
        var e = Engine(f);
        _ = e.Misspelled("wrng");
        e.Reload();
        Assert.Equal(0, e.CachedTexts);
        _ = e.Misspelled("wrng");
        Assert.Equal(2, f.Created.Count);
    }

    [Fact]
    public void The_default_engine_marks_nothing()
    {
        var e = new NoSpellEngine();
        Assert.False(e.IsAvailable);
        Assert.Empty(e.Misspelled("wrng"));
        Assert.Null(new NoSpellCheckerFactory().Create("en-US"));
        Assert.Equal("", new NoSpellCheckerFactory().UserLocale);
    }

    [Theory]
    [InlineData("en", "x", "en-US")]
    [InlineData("de", "x", "de-DE")]
    [InlineData("fr", "x", "fr-FR")]
    [InlineData("es", "x", "es-ES")]
    [InlineData("tr", "x", "tr-TR")]
    [InlineData("auto", "pt-BR", "pt-BR")]
    [InlineData("", "pt-BR", "pt-BR")]
    [InlineData("", "", "en-US")]
    [InlineData("nl-NL", "x", "nl-NL")]
    public void Language_tags(string pref, string locale, string expected) => Assert.Equal(expected, SpellText.LanguageTag(pref, locale));

    [Fact]
    public void To_text_ranges_drops_empty_negative_and_overlong_ranges_and_keeps_order()
    {
        const string t = "abcdef";
        Assert.Equal([(0, 6)], SpellText.ToTextRanges(t, [new SpellRange(0, 6)]));
        Assert.Empty(SpellText.ToTextRanges(t, [new SpellRange(0, 7)]));
        Assert.Empty(SpellText.ToTextRanges(t, [new SpellRange(-1, 2)]));
        Assert.Empty(SpellText.ToTextRanges(t, [new SpellRange(6, 1)]));
        Assert.Empty(SpellText.ToTextRanges(t, [new SpellRange(int.MaxValue, int.MaxValue)]));
        Assert.Equal([(4, 5), (1, 3)], SpellText.ToTextRanges(t, [new SpellRange(4, 1), new SpellRange(1, 2), new SpellRange(5, 0)]));
        Assert.Empty(SpellText.ToTextRanges("", [new SpellRange(0, 1)]));
    }

    [Fact]
    public void Skip_word_and_token_edge_cases()
    {
        Assert.True(SpellText.SkipWord("www.x.example"));
        Assert.True(SpellText.SkipWord("a"));
        Assert.True(SpellText.SkipWord(""));
        Assert.True(SpellText.SkipWord("\U0001F600"));
        Assert.False(SpellText.SkipWord("ab"));
        Assert.False(SpellText.SkipWord("été"));
        Assert.Equal("word", SpellText.Token("word", 0, 4));
        Assert.Equal("b", SpellText.Token("a b c", 2, 3));
        Assert.Equal("", SpellText.Token("", 0, 0));
        Assert.Equal("abc", SpellText.Token("x\tabc\ny", 2, 3));
    }

    [Fact]
    public void The_composition_builds_the_engine_from_the_prefs_and_the_registered_factory()
    {
        var f = new FakeFactory("en-GB");
        var prefs = new FakePrefs(new JsonObject { ["writingLanguage"] = "de" });
        var services = new ServiceCollection().AddBarbicanEditorCore();
        var map = new Dictionary<Type, object> { [typeof(IPrefsStore)] = prefs, [typeof(ISpellCheckerFactory)] = f };
        var sp = new MapProvider(map);
        var descriptor = services.Last(d => d.ServiceType == typeof(ISpellEngine));
        var engine = (ISpellEngine)descriptor.ImplementationFactory!(sp);
        Assert.IsType<SpellEngine>(engine);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ISpellEngine) && d.ImplementationType == typeof(NoSpellEngine));
        _ = engine.Misspelled("wrng");
        Assert.Equal("de-DE", f.Created[0].Tag);

        prefs.Set(new JsonObject { ["spellCheck"] = false });
        Assert.False(engine.IsAvailable);
        Assert.NotNull(services.Single(d => d.ServiceType == typeof(IEditorSessionFactory)));
    }

    [Fact]
    public void Spell_prefs_default_to_on_and_auto()
    {
        var s = EditorCoreRegistration.ReadSpellSettings(PrefsSnapshot.FromJson([]));
        Assert.True(s.Enabled);
        Assert.Equal("auto", s.Language);
    }

    private sealed class MapProvider(Dictionary<Type, object> map) : IServiceProvider
    {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }

    private sealed class FakePrefs(JsonObject values) : IPrefsStore
    {
        private PrefsSnapshot _current = PrefsSnapshot.FromJson(values);

        public event EventHandler? Changed;

        public PrefsSnapshot Current => _current;

        public void Set(JsonObject v)
        {
            _current = PrefsSnapshot.FromJson(v);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
