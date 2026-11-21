// Mirrors: Bulwark lib/proofreading/languages.ts and the writingLanguage setting ("auto" follows the system language). No Stalwart counterpart.
using System.Globalization;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>The language a checker is asked to check in.</summary>
public static class PluginCheckLanguage
{
    public static string Resolve(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        var language = prefs.Str("writingLanguage", "auto");
        return string.Equals(language, "auto", StringComparison.Ordinal)
            ? CultureInfo.CurrentCulture.Name is { Length: > 0 } configured ? configured : "en"
            : language;
    }
}
