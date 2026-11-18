// Mirrors: Rust shortcuts.rs:204 contacts keys (the search key, Shift+F10); Bulwark contact-list.tsx. B-CON-031. Owner con-ui: what a contacts key needs from the screen (focus a field, open a menu at the selected row).

namespace Barbican.Presentation.Contacts;

/// <summary>The screen's part of the contacts keys.</summary>
public interface IContactsChrome
{
    /// <summary>Puts the cursor in the search field.</summary>
    void FocusSearch();

    /// <summary>Opens MENU-CONTACT on the selected row.</summary>
    void ShowSelectionMenu();
}
