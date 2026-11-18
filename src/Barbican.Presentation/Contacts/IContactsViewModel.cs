// Mirrors: Rust contacts.rs (ST, select, filter, bulk), Bulwark components/contacts/contacts-app.tsx, contact-list.tsx, contacts-sidebar.tsx; I-345..I-358, I-444. Hooks: onContactOpen, onContactSelectionChange.
// Contract from the M5 plan section 4.5; owner con-ui implements; the contact menus, the form and the dialogs (all con-ui) read it.

using System.ComponentModel;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Contacts;

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

    /// <summary>The groupContactsByLetter setting, applied live.</summary>
    bool GroupByLetter { get; }

    bool IsLoading { get; }

    /// <summary>The load error with a Retry, or null.</summary>
    string? LoadError { get; }

    /// <summary>The login the app shows, or null.</summary>
    string? AccountKey { get; }

    IReadOnlyList<ContactsSidebarEntry> Books { get; }

    IReadOnlyList<ContactsSidebarEntry> Groups { get; }

    IReadOnlyList<ContactsSidebarEntry> Categories { get; }

    /// <summary>The Directory entry shows: the principals capability and showContactsDirectory (D-M5-13).</summary>
    bool DirectoryAvailable { get; }

    /// <summary>How many sets of probable duplicates the contacts hold.</summary>
    int DuplicateSetCount { get; }

    /// <summary>The select mode of the list (checkboxes and the bulk bar, I-353).</summary>
    bool SelectMode { get; set; }

    /// <summary>The contact in the detail pane, or null.</summary>
    string? OpenId { get; }

    JsonNode? OpenCard { get; }

    /// <summary>The detail pane's lines of <see cref="OpenCard"/>.</summary>
    IReadOnlyList<ContactLine> DetailLines { get; }

    /// <summary>The card (or group) with this id in the account shown, or null.</summary>
    JsonNode? CardOf(string id);

    /// <summary>Every contact (groups left out) of the account shown.</summary>
    IReadOnlyList<JsonNode> Contacts { get; }

    /// <summary>Every group card of the account shown.</summary>
    IReadOnlyList<JsonNode> GroupCards { get; }

    /// <summary>The sets of probable duplicates (ContactMerge.FindGroups over the contacts).</summary>
    IReadOnlyList<IReadOnlyList<JsonNode>> DuplicateSets { get; }

    void ShowPlace(ContactsPlace place, string? placeId);

    /// <summary>Selecting is not opening; opening (the detail pane) fires onContactOpen.</summary>
    void SelectContact(string id, bool extend);

    void Open(string id);

    /// <summary>Moves the selection by <paramref name="delta"/> rows (B-CON-031: arrows).</summary>
    void Move(int delta);

    /// <summary>Selects the first (or last) row (Home, End).</summary>
    void Jump(bool last);

    /// <summary>Esc: closes the detail pane and clears the selection, or the search when it has text.</summary>
    void Clear();

    void SelectAll();

    /// <summary>Deletes the cards (the caller has asked the user); the list follows the store.</summary>
    Task<Result<Unit>> DeleteAsync(IReadOnlyList<string> ids, CancellationToken ct);

    Task RefreshAsync(CancellationToken ct);

    /// <summary>Whether the contacts of this account were loaded (the screen loads on its first visit; the form loads before it opens from the mail side).</summary>
    bool IsLoadedFor(string accountKey);
}
