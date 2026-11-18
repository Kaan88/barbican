// New tests (M5 plan 6.2 acceptance): the list over the store, places, search, sort and letter settings applied live, selection versus opening, keys, delete, the two hooks. Owner con-ui.

#pragma warning disable MA0002, MA0006 // ids and names are compared ordinally by nature
using Barbican.Core.Plugins.Hooks;
using Barbican.Presentation.Contacts;

namespace Barbican.Presentation.Tests.Contacts;

public sealed class ContactsViewModelTests
{
    [Fact]
    public void The_list_holds_the_contacts_sorted_with_letters_and_groups_stay_in_the_sidebar()
    {
        using var h = new ContactsHarness();
        var rows = h.View.Rows;
        Assert.Equal(["Ada Lovelace", "Charles Babbage", "Émile Zola"], rows.Select(r => r.DisplayName));
        Assert.Equal(["A", "C", "E"], rows.Select(r => r.Letter));
        Assert.Equal(["AL", "CB", "EZ"], rows.Select(r => r.Initials));
        Assert.Single(h.View.Groups);
        Assert.Equal(2, h.View.Groups[0].Count);
        Assert.Equal(2, h.View.Books.Single(b => b.Id == "b1").Count);
        Assert.True(h.View.Books.Single(b => b.Id == "b1").IsDefault);
        Assert.Equal("Friends", Assert.Single(h.View.Categories).Name);
    }

    [Fact]
    public void Places_filter_the_list()
    {
        using var h = new ContactsHarness();
        h.View.ShowPlace(ContactsPlace.Book, "b2");
        Assert.Equal(["c2"], h.View.Rows.Select(r => r.Id));
        h.View.ShowPlace(ContactsPlace.Group, "g1");
        Assert.Equal(["c1", "c3"], h.View.Rows.Select(r => r.Id).Order(StringComparer.Ordinal));
        h.View.ShowPlace(ContactsPlace.Category, "Friends");
        Assert.Equal(2, h.View.Rows.Count);
        h.View.ShowPlace(ContactsPlace.All, null);
        Assert.Equal(3, h.View.Rows.Count);
    }

    [Fact]
    public void A_place_that_disappears_falls_back_to_all()
    {
        using var h = new ContactsHarness();
        h.View.ShowPlace(ContactsPlace.Group, "g1");
        _ = h.Store.Cards.RemoveAll(c => ContactsJson.Id(c) == "g1");
        h.Store.Raise();
        Assert.Equal(ContactsPlace.All, h.View.Place);
        Assert.Equal(3, h.View.Rows.Count);
    }

    [Fact]
    public void Search_filters_and_clear_empties_it_first()
    {
        using var h = new ContactsHarness();
        h.View.SearchText = "zola";
        Assert.Equal(["c2"], h.View.Rows.Select(r => r.Id));
        h.View.Clear();
        Assert.Equal(string.Empty, h.View.SearchText);
        Assert.Equal(3, h.View.Rows.Count);
    }

    [Fact]
    public void The_sort_and_letter_settings_apply_live()
    {
        using var h = new ContactsHarness();
        Assert.Equal("Ada Lovelace", h.View.Rows[0].SortKey);
        h.Prefs.Set("""{"sortContactsByLastName": true, "groupContactsByLetter": false}""");
        Assert.True(h.View.SortByLastName);
        Assert.False(h.View.GroupByLetter);
        Assert.Equal(["Babbage, Charles", "Lovelace, Ada", "Zola, Émile"], h.View.Rows.Select(r => r.SortKey));
        Assert.Equal(["B", "L", "Z"], h.View.Rows.Select(r => r.Letter));
    }

    [Fact]
    public async Task Selecting_is_not_opening_and_opening_fires_the_hook()
    {
        using var h = new ContactsHarness();
        var opens = new List<ContactsOpenPayload>();
        using var sub = h.Bus.Observe<ContactsOpenPayload>(HookNames.OnContactOpen, (p, _) => { lock (opens) { opens.Add(p); } return ValueTask.CompletedTask; });
        h.View.SelectContact("c1", false);
        Assert.Equal(["c1"], h.View.SelectedIds);
        Assert.Null(h.View.OpenId);
        h.View.SelectContact("c3", true);
        Assert.Equal(2, h.View.SelectedIds.Count);
        h.View.SelectContact("c3", true);
        Assert.Equal(["c1"], h.View.SelectedIds);
        h.View.Open("c1");
        Assert.Equal("c1", h.View.OpenId);
        Assert.Equal("Ada Lovelace", ContactsListRules.FullName(h.View.OpenCard!));
        await ContactsHarness.EventuallyAsync(() => opens.Count == 1);
        Assert.Equal("c1", Assert.Single(opens).Id);
    }

    [Fact]
    public async Task The_selection_hook_carries_the_ids()
    {
        using var h = new ContactsHarness();
        var seen = new List<ContactsSelectionPayload>();
        using var sub = h.Bus.Observe<ContactsSelectionPayload>(HookNames.OnContactSelectionChange, (p, _) => { lock (seen) { seen.Add(p); } return ValueTask.CompletedTask; });
        h.View.SelectContact("c3", false);
        h.View.SelectContact("c1", true);
        await ContactsHarness.EventuallyAsync(() => seen.Count == 2);
        Assert.Equal(["c1", "c3"], seen[^1].ContactIds);
    }

    [Fact]
    public void The_keys_move_open_jump_and_clear()
    {
        using var h = new ContactsHarness();
        h.View.Move(1);
        Assert.Equal("c1", h.View.OpenId);
        h.View.Move(1);
        Assert.Equal("c3", h.View.OpenId);
        h.View.Move(5);
        Assert.Equal("c2", h.View.OpenId);
        h.View.Jump(false);
        Assert.Equal("c1", h.View.OpenId);
        h.View.Clear();
        Assert.Null(h.View.OpenId);
        Assert.Empty(h.View.SelectedIds);
    }

    [Fact]
    public async Task Delete_goes_through_the_writer_and_the_list_follows()
    {
        using var h = new ContactsHarness();
        h.View.SelectContact("c1", false);
        var result = await h.View.DeleteAsync(["c1"], CancellationToken.None);
        Assert.True(result.IsOk);
        Assert.Equal(["c1"], h.Writer.Deleted);
        Assert.Equal(["c3", "c2"], h.View.Rows.Select(r => r.Id));
        Assert.Empty(h.View.SelectedIds);
    }

    [Fact]
    public async Task Refresh_loads_the_store_and_a_login_change_reloads()
    {
        using var h = new ContactsHarness();
        await h.View.RefreshAsync(CancellationToken.None);
        Assert.Equal(1, h.Store.Loads);
        Assert.True(h.View.IsLoadedFor("acc"));
        h.Account.Switch("other");
        Assert.Equal(2, h.Store.Loads);
    }
}
