// Mirrors: B-CON-031 (the search key focuses the field, Shift+F10 opens the menu of the selected row); see IContactsChrome. The commands exist before the screen does, so this reaches the screen once it is built.

using Barbican.Presentation.Contacts;

namespace Barbican.App.Views.Contacts;

/// <summary>The keys' view of the Contacts screen.</summary>
public sealed class ContactsScreenChrome : IContactsChrome
{
    public void FocusSearch() => ContactsScreen.Current?.FocusSearch();

    public void ShowSelectionMenu() => ContactsScreen.Current?.List.ShowSelectionMenu();
}
