// Mirrors: M3 plan section 4.2 (DI skeleton). Owner editor-core: IEditorSessionFactory (the real one replaces the stub), ISpellCheckerFactory default.

using Barbican.Core.Editor.Spell;
using Barbican.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Core.Editor;

/// <summary>Registers the editor core's services (M3 stream editor-core).</summary>
public static class EditorCoreRegistration
{
    public static IServiceCollection AddBarbicanEditorCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISpellCheckerFactory, NoSpellCheckerFactory>();

        // the cached engine replaces the empty default; it asks the registered factory (Windows: ISpellChecker, elsewhere none until M9) and follows the spellCheck and writingLanguage prefs
        services.Replace(ServiceDescriptor.Singleton<ISpellEngine>(sp =>
        {
            var prefs = sp.GetRequiredService<IPrefsStore>();
            return new SpellEngine(sp.GetRequiredService<ISpellCheckerFactory>(), () => ReadSpellSettings(prefs.Current));
        }));
        services.TryAddSingleton<IEditorSessionFactory, EditorSessionFactory>();
        return services;
    }

    /// <summary>The spell preferences: <c>spellCheck</c> (default on) and <c>writingLanguage</c> (default "auto").</summary>
    public static SpellSettings ReadSpellSettings(PrefsSnapshot prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return new SpellSettings(prefs.Bool("spellCheck", true), prefs.Str("writingLanguage", "auto"));
    }
}
