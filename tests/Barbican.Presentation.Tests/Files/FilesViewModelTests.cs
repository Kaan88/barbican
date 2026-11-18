// Not Rust rows: B-FILE-012, B-FILE-014, G-015 (places, selection, search, sort of the Files view model). M5 stream files-ui.

#pragma warning disable MA0006, MA0002, CA1861 // assertions compare string literals and literal arrays

using Barbican.Core.Files;
using Barbican.Presentation.Files;

namespace Barbican.Presentation.Tests.Files;

public sealed class FilesViewModelTests
{
    private static FileEntry E(string key, string? parent, string name, bool dir, bool shared = false) =>
        new(key, "a", shared, key, parent, name, dir, dir ? null : "b", 1, null, null, null);

    private static (FilesViewModel Vm, FakeFileStore Store) Make()
    {
        var store = new FakeFileStore(
        [
            E("d1", null, "Docs", true),
            E("d2", "d1", "Sub", true),
            E("f1", null, "b.txt", false),
            E("f2", null, "a10.txt", false),
            E("f3", null, "a2.txt", false),
            E("f4", "d1", "inner.txt", false),
            E("h", null, ".hidden", false),
            E("s1", null, "Shared dir", true, shared: true),
        ]);
        return (new FilesViewModel(store, new FakeFileWriter()) { AccountKey = "a" }, store);
    }

    [Fact]
    public void The_root_lists_my_files_sorted_with_folders_first_and_hides_dot_files()
    {
        var (vm, _) = Make();
        Assert.Equal(["Docs", "a2.txt", "a10.txt", "b.txt"], vm.Entries.Select(e => e.Name));
        vm.ShowHidden = true;
        Assert.Contains(vm.Entries, e => e.Name == ".hidden");
    }

    [Fact]
    public void Navigating_opens_a_folder_and_builds_the_breadcrumb()
    {
        var (vm, _) = Make();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Navigate("d2");
        Assert.Equal("d2", vm.Location);
        Assert.Empty(vm.Entries);
        Assert.Equal(["Docs", "Sub"], vm.Breadcrumb.Select(e => e.Name));
        Assert.Contains("Location", raised);
        vm.Navigate("d1");
        Assert.Equal(["Sub", "inner.txt"], vm.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Favourites_recent_and_shared_are_places()
    {
        var (vm, store) = Make();
        store.ToggleStar("f1");
        store.AddRecent("f3");
        store.AddRecent("f2");
        vm.Navigate(FilesViewModel.FavouritesLocation);
        Assert.Equal(["b.txt"], vm.Entries.Select(e => e.Name));
        vm.Navigate(FilesViewModel.RecentLocation);
        Assert.Equal(["a10.txt", "a2.txt"], vm.Entries.Select(e => e.Name));
        vm.Navigate(FilesViewModel.SharedLocation);
        Assert.Equal(["Shared dir"], vm.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Click_replaces_ctrl_toggles_shift_selects_a_range_and_selection_prunes_with_the_list()
    {
        var (vm, _) = Make();
        vm.SelectFile("f3", false);
        vm.SelectFile("f1", true);
        Assert.Equal(["f1", "f3"], vm.SelectedKeys.Order());
        vm.SelectFile("f3", true);
        Assert.Equal("f1", Assert.Single(vm.SelectedKeys));
        vm.SelectFile("d1", false);
        vm.SelectRange("f2");
        Assert.Equal(3, vm.SelectedKeys.Count);
        vm.SearchText = "b.t";
        Assert.Empty(vm.SelectedKeys);
        Assert.Equal(["b.txt"], vm.Entries.Select(e => e.Name));
    }

    [Fact]
    public void Sort_key_and_direction_reorder_the_list()
    {
        var (vm, _) = Make();
        vm.SortDescending = true;
        Assert.Equal(["Docs", "b.txt", "a10.txt", "a2.txt"], vm.Entries.Select(e => e.Name));
    }

    [Fact]
    public void A_store_change_for_the_account_refreshes_the_list()
    {
        var (vm, store) = Make();
        store.Entries = [.. store.Entries, E("f9", null, "new.txt", false)];
        store.Raise("a");
        Assert.Contains(vm.Entries, e => e.Name == "new.txt");
    }
}
