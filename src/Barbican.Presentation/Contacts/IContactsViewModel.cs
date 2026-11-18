// Mirrors: Rust contacts.rs (ST, select, filter, bulk), Bulwark components/contacts/contacts-app.tsx, contact-list.tsx, contacts-sidebar.tsx; I-345..I-358, I-444. Hooks: onContactOpen, onContactSelectionChange.
// Contract from the M5 plan section 4.5; owner con-ui implements; the contact menus, the form and the dialogs (all con-ui) read it.

using System.ComponentModel;

namespace Barbican.Presentation.Contacts;

/// <summary>The state of the contacts surface: the place, the list, the selection, search and sort. One per window; <c>PropertyChanged</c> is raised on the UI thread.</summary>
public interface IContactsViewModel : INotifyPropertyChanged
{
    ContactsPlace Place { get; }

    /// <summary>The book, group or category the place names (null for All, Directory, Duplicates).</summary>
    string? PlaceId { get; }

    IReadOnlyList<ContactsRow> Rows { get; }

    IReadOnlySet<string> SelectedIds { get; }

    string SearchText { get; set; }

    /// <summary>The sortContactsByLastName setting, applied live.</summary>
    bool SortByLastName { get; }

    bool IsLoading { get; }

    /// <summary>The load error with a Retry, or null.</summary>
    string? LoadError { get; }

    void ShowPlace(ContactsPlace place, string? placeId);

    /// <summary>Selecting is not opening; opening (the detail pane) fires onContactOpen.</summary>
    void SelectContact(string id, bool extend);

    void Open(string id);

    Task RefreshAsync(CancellationToken ct);
}
