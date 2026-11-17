// Mirrors: Rust src/rte/spell.rs config() (prefs spellCheck, writingLanguage); Bulwark settings writingLanguage. Contract addition of editor-core (additive).

namespace Barbican.Core.Editor.Spell;

/// <summary>What the spell engine reads from the preferences.</summary>
/// <param name="Enabled">The <c>spellCheck</c> preference (default on).</param>
/// <param name="Language">The <c>writingLanguage</c> preference: "auto", a short code or a BCP 47 tag.</param>
public sealed record SpellSettings(bool Enabled, string Language)
{
    public static SpellSettings Default { get; } = new(Enabled: true, Language: "auto");
}
