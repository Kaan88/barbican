// Not Rust rows: Bulwark components/files/files-header.tsx:431-452 (the sort menu: a new key starts ascending for Name and descending for Modified and Size; Ascending and Descending set only the direction). WP-14, FL-01.

#pragma warning disable MA0006, MA0002 // assertions compare string literals

using Barbican.Presentation.Commands;
using Barbican.Presentation.Files;
using Barbican.Presentation.Menus;

namespace Barbican.Presentation.Tests.Files;

public sealed class FilesSortTests
{
    private static FilesViewModel Vm() => new(new FakeFileStore([]), new FakeFileWriter()) { AccountKey = "a" };

    [Fact]
    public void Choosing_a_new_key_picks_its_default_direction_and_the_same_key_keeps_the_direction()
    {
        var vm = Vm();
        FilesSortRules.Apply(vm, "modified");
        Assert.Equal((FilesSortKey.Modified, true), (vm.SortKey, vm.SortDescending));
        FilesSortRules.Apply(vm, "asc");
        FilesSortRules.Apply(vm, "modified");
        Assert.False(vm.SortDescending);
        FilesSortRules.Apply(vm, "name");
        Assert.Equal((FilesSortKey.Name, false), (vm.SortKey, vm.SortDescending));
        FilesSortRules.Apply(vm, "nonsense");
        Assert.Equal(FilesSortKey.Name, vm.SortKey);
    }

    [Fact]
    public void The_sort_menu_checks_the_current_key_and_direction()
    {
        var store = new FakeFileStore([]);
        var vm = new FilesViewModel(store, new FakeFileWriter()) { AccountKey = "a" };
        var a = new FilesActions(vm, store, new FakeFileWriter(), toasts: new FakeToasts());
        FilesSortRules.Apply(vm, "size");
        var menu = FilesMenus.Create(a).Single(p => p.MenuId == FilesMenuIds.Sort).Build(new MenuContext(FilesMenuIds.Sort, [], null, null, null, null, CommandSurface.Toolbar));
        Assert.Equal(["Size", "Descending"], menu.Where(i => i.Checked == true).Select(i => i.Label));
    }
}
