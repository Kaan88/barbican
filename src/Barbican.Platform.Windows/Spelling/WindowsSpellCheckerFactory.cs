// Mirrors: Rust src/rte/spell.rs:49-120 (sys::create over SpellCheckerFactory, GetUserDefaultLocaleName; an unsupported tag falls back to <base>-<BASE>, then en-US);
// Windows spellcheck.h (ISpellCheckerFactory::IsSupported, CreateSpellChecker). Unpackaged apps can create the factory.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Barbican.Core.Editor.Spell;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>The Windows spell checker behind <see cref="ISpellCheckerFactory"/> (the engine Edge and WebView2 use).</summary>
public sealed unsafe class WindowsSpellCheckerFactory : ISpellCheckerFactory
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    public string UserLocale
    {
        get
        {
            var buf = stackalloc char[85];
            var n = SpellingNative.GetUserDefaultLocaleName(buf, 85);
            return n <= 1 ? "" : new string(buf, 0, n - 1);
        }
    }

    public Barbican.Core.Editor.Spell.ISpellChecker? Create(string languageTag)
    {
        ArgumentNullException.ThrowIfNull(languageTag);
        try
        {
            // a thread already initialised with another apartment model is fine: the result is ignored, as in Rust
            _ = SpellingNative.CoInitializeEx(0, SpellingNative.ComApartmentThreaded);
            if (CreateFactory() is not { } factory)
            {
                return null;
            }

            var tag = languageTag;
            if (!Supported(factory, tag))
            {
                // "de-AT" becomes any supported "de-DE", else English
                var b = tag.Split('-')[0];
                var fallback = new[] { b + "-" + b.ToUpperInvariant(), "en-US" }.FirstOrDefault(t => Supported(factory, t));
                if (fallback is null)
                {
                    return null;
                }

                tag = fallback;
            }

            return factory.CreateSpellChecker(tag, out var checker) == 0 && checker is not null ? new WindowsSpellChecker(checker) : null;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Supported(ISpellCheckerFactoryCom f, string tag) => f.IsSupported(tag, out var ok) == 0 && ok;

    private static ISpellCheckerFactoryCom? CreateFactory()
    {
        var iid = typeof(ISpellCheckerFactoryCom).GUID;
        if (SpellingNative.CoCreateInstance(SpellingNative.SpellCheckerFactoryClass, 0, SpellingNative.ClsctxInprocServer, iid, out var ptr) != 0 || ptr == 0)
        {
            return null;
        }

        return (ISpellCheckerFactoryCom)Wrappers.GetOrCreateObjectForComInstance(ptr, CreateObjectFlags.UniqueInstance);
    }
}
