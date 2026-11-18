// Mirrors: see IContactsViewModel. Stub from the M5 plan section 4.5; owner con-ui replaces the class (constructor dependencies: IContactStore, IContactWriter, ISettingsStore, IHookBus, TimeProvider).

using System.ComponentModel;
using Barbican.Core.Common;

namespace Barbican.Presentation.Contacts;

/// <summary>The default <see cref="IContactsViewModel"/>.</summary>
public sealed class ContactsViewModel : IContactsViewModel
{
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    public ContactsPlace Place => throw new PortPendingException("ContactsViewModel.Place");

    public string? PlaceId => throw new PortPendingException("ContactsViewModel.PlaceId");

    public IReadOnlyList<ContactsRow> Rows => throw new PortPendingException("ContactsViewModel.Rows");

    public IReadOnlySet<string> SelectedIds => throw new PortPendingException("ContactsViewModel.SelectedIds");

    public string SearchText
    {
        get => throw new PortPendingException("ContactsViewModel.SearchText");
        set => throw new PortPendingException("ContactsViewModel.SearchText");
    }

    public bool SortByLastName => throw new PortPendingException("ContactsViewModel.SortByLastName");

    public bool IsLoading => throw new PortPendingException("ContactsViewModel.IsLoading");

    public string? LoadError => throw new PortPendingException("ContactsViewModel.LoadError");

    public void ShowPlace(ContactsPlace place, string? placeId) => throw new PortPendingException("ContactsViewModel.ShowPlace");

    public void SelectContact(string id, bool extend) => throw new PortPendingException("ContactsViewModel.SelectContact");

    public void Open(string id) => throw new PortPendingException("ContactsViewModel.Open");

    public Task RefreshAsync(CancellationToken ct) => throw new PortPendingException("ContactsViewModel.RefreshAsync");
}
