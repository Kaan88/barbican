// Not Rust rows: G-076 (the composer's attach-from-Files picker). Bulwark components/files/file-picker-dialog.tsx:31-135 (levels, shared roots, search, folders first).
// M6 stream composer-ui.

#pragma warning disable MA0006, MA0002 // assertions compare string literals by nature

using Barbican.Core.Common;
using Barbican.Core.Files;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Tests.Files;

namespace Barbican.Presentation.Tests.Compose;

public sealed class ComposerFilesPickerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FileEntry E(string key, string? parent, string name, bool dir, long size = 0, bool shared = false) =>
        new(key, "a", shared, key, parent, name, dir, dir ? null : "blob-" + key, size, null, dir ? null : "text/plain", null);

    private static FakeFileStore Store() => new(
    [
        E("d1", null, "Docs", true),
        E("d2", "d1", "Plans", true),
        E("d3", null, "Empty", true),
        E("f1", "d2", "notes.txt", false, 120),
        E("f2", null, "a.txt", false, 10),
        E("f3", null, "a10.txt", false, 20),
        E("h1", null, ".secret", false, 1),
        E("s1", null, "Shared folder", true, shared: true),
        E("s2", "s1", "theirs.txt", false, 30, shared: true),
        E("s3", null, "loose.txt", false, 40, shared: true),
    ]);

    private static async Task<ComposerFilesPicker> PickerAsync(FakeFileStore? store = null, bool showHidden = false)
    {
        var picker = new ComposerFilesPicker(store ?? Store(), "a", showHidden);
        await picker.LoadAsync(Ct);
        return picker;
    }

    [Fact]
    public async Task The_root_lists_my_folders_first_then_files_with_shared_with_me_on_top()
    {
        var picker = await PickerAsync();
        Assert.Equal(["Shared with me", "Docs", "Empty", "a.txt", "a10.txt"], picker.Rows.Select(r => r.Name));
        Assert.True(picker.Rows[0].IsFolder);
        Assert.True(picker.AtRoot);
        Assert.False(picker.Searching);
    }

    [Fact]
    public async Task Opening_a_folder_shows_its_level_and_the_trail_goes_back()
    {
        var picker = await PickerAsync();
        picker.OpenFolder("d1", "Docs");
        Assert.Equal(["Plans"], picker.Rows.Select(r => r.Name));
        Assert.Equal(["Docs"], picker.Trail.Select(c => c.Name));
        Assert.False(picker.AtRoot);

        picker.OpenFolder("d2", "Plans");
        Assert.Equal(["notes.txt"], picker.Rows.Select(r => r.Name));
        Assert.Equal(["Docs", "Plans"], picker.Trail.Select(c => c.Name));

        picker.GoToCrumb(0);
        Assert.Equal(["Docs"], picker.Trail.Select(c => c.Name));
        picker.OpenRoot();
        Assert.Empty(picker.Trail);
        Assert.True(picker.AtRoot);
    }

    [Fact]
    public async Task Shared_with_me_lists_what_was_shared_from_another_account()
    {
        var picker = await PickerAsync();
        picker.OpenFolder(ComposerFilesPicker.SharedLocation, "Shared with me");
        Assert.Equal(["Shared folder", "loose.txt"], picker.Rows.Select(r => r.Name));
        Assert.Equal("Shared with me", picker.Trail[^1].Name);
    }

    [Fact]
    public async Task Search_looks_at_every_file_and_says_where_it_lives()
    {
        var picker = await PickerAsync();
        picker.SearchText = "notes";
        Assert.Equal(["notes.txt"], picker.Rows.Select(r => r.Name));
        Assert.Equal("Docs / Plans", picker.Rows[0].Detail);
        Assert.True(picker.Searching);

        picker.SearchText = "txt";
        Assert.DoesNotContain(picker.Rows, r => r.IsFolder);
        Assert.Contains(picker.Rows, r => r.Name == "theirs.txt");
        Assert.Equal(["a.txt", "a10.txt", "loose.txt", "notes.txt", "theirs.txt"], picker.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Search_brings_hidden_names_back_only_when_the_files_setting_allows_them()
    {
        var plain = await PickerAsync();
        plain.SearchText = "secret";
        Assert.Empty(plain.Rows);
        Assert.Equal("No files match your search", plain.EmptyText);

        var hidden = await PickerAsync(showHidden: true);
        hidden.SearchText = "secret";
        Assert.Equal([".secret"], hidden.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task Ticking_picks_files_in_order_and_never_a_folder()
    {
        var picker = await PickerAsync();
        picker.OpenFolder("d2", "Plans");
        picker.Select("f1", true);
        Assert.True(picker.IsSelected("f1"));
        Assert.True(picker.CanAttach);
        Assert.Equal("Attach 1 file", picker.AttachLabel);
        Assert.Equal(["notes.txt"], picker.Picked.Select(e => e.Name));

        picker.OpenRoot();
        picker.Select("f2", true);
        picker.Select("d1", true);
        Assert.Equal(["notes.txt", "a.txt"], picker.Picked.Select(e => e.Name));
        Assert.Equal("Attach 2 files", picker.AttachLabel);

        picker.Select("f1", false);
        Assert.Equal(["a.txt"], picker.Picked.Select(e => e.Name));
        picker.Select("f2", false);
        Assert.False(picker.CanAttach);
        Assert.Equal("Attach", picker.AttachLabel);
    }

    [Fact]
    public async Task An_empty_folder_and_an_empty_search_say_so_differently()
    {
        var picker = await PickerAsync();
        picker.OpenFolder("d3", "Empty");
        Assert.Empty(picker.Rows);
        Assert.Equal("This folder is empty", picker.EmptyText);
        picker.SearchText = "nothing";
        Assert.Empty(picker.Rows);
        Assert.Equal("No files match your search", picker.EmptyText);
    }

    [Fact]
    public async Task A_store_that_cannot_load_says_so_for_retry()
    {
        var picker = new ComposerFilesPicker(new BrokenStore(), "a");
        await picker.LoadAsync(Ct);
        Assert.True(picker.LoadFailed);
        Assert.False(picker.IsLoading);
        Assert.Empty(picker.Rows);
    }

    // a store with no snapshot that fails every load (the "Your files could not be loaded" card)
    private sealed class BrokenStore : IFileStore
    {
        public event EventHandler<FileChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<string> Favourites => [];

        public IReadOnlyList<string> Recent => [];

        public Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct) =>
            Task.FromResult(Result<FileSnapshot>.Fail(new ValidationError("no files")));

        public FileSnapshot? Snapshot(string accountKey) => null;

        public bool Supports(string accountKey) => true;

        public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));

        public void ToggleStar(string key)
        {
        }

        public void AddRecent(string key)
        {
        }

        public void RemoveRecent(string key)
        {
        }
    }
}
