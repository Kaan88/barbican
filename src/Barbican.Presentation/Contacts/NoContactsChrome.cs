// Mirrors: see IContactsChrome. The default while the Contacts screen has not been built (and in tests). Owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>A screen that does nothing.</summary>
public sealed class NoContactsChrome : IContactsChrome
{
    public void FocusSearch()
    {
    }

    public void ShowSelectionMenu()
    {
    }
}
