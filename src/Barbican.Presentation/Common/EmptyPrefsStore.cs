// Mirrors: M1-plan §4.2 (IPrefsStore; accounts replaces this default in wave 2). Owner mailcore.

using Barbican.Core.Storage;

namespace Barbican.Presentation.Common;

/// <summary>Preferences with nothing stored: every setting has its default. Registered with TryAdd until the real store arrives.</summary>
public sealed class EmptyPrefsStore : IPrefsStore
{
    public PrefsSnapshot Current { get; } = PrefsSnapshot.FromJson([]);

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
