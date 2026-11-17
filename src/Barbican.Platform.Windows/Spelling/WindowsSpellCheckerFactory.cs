// Mirrors: Rust src/rte/spell.rs:49-120 (sys::Checker over ISpellChecker, SpellCheckerFactory, GetUserDefaultLocaleName); Windows spellcheck.h (ISpellCheckerFactory::CreateSpellChecker, ISpellChecker::Check/Suggest/Add/Ignore). Contract stub from the M3 plan section 4.2; owner editor-core replaces the body (CsWin32 or COM interop).

using Barbican.Core.Common;
using Barbican.Core.Editor.Spell;

namespace Barbican.Platform.Windows.Spelling;

/// <summary>The Windows spell checker behind <see cref="ISpellCheckerFactory"/>. Stub: throws <see cref="PortPendingException"/> until stream editor-core lands.</summary>
public sealed class WindowsSpellCheckerFactory : ISpellCheckerFactory
{
    public string UserLocale => throw new PortPendingException("WindowsSpellCheckerFactory.UserLocale");

    public ISpellChecker? Create(string languageTag) => throw new PortPendingException("WindowsSpellCheckerFactory.Create");
}
