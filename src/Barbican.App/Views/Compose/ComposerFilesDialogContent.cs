// Mirrors: Bulwark components/files/file-picker-dialog.tsx:46-316 in the default theme: the search box over a folder path ("My files" and the crumbs), a scrollable list
// (folders with a chevron open, files with a check and their size tick), "This folder is empty" / "No files match your search" / "No files yet", the failure line with Retry,
// and a footer Cancel / "Attach N files" (h-10 buttons, ui/dialog.tsx:132-134). The listing, the search and the ticks are ComposerFilesPicker; this only draws them.
// Drawn by the shared dialog host (IDialogContent), opened by the composer's Attach menu (components/email/email-composer.tsx:6467-6475). Owner composer-ui.

using Barbican.App.Theme;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Compose;

/// <summary>Draws <see cref="ComposerFilesRequest"/>: the attach-from-Files picker.</summary>
public sealed class ComposerFilesDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is ComposerFilesRequest;

    public bool OwnButtons => true;

    // the card of Bulwark's modal (max-w-lg is 512; the dialog card padding gives the rest)
    public double MaxWidth => 520;

    public Control? InitialFocus(UIElement content) => (content as FrameworkElement)?.Tag as Control;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var picker = ((ComposerFilesRequest)dialog.Request).Picker;
        var search = SearchBox();
        var crumbs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var list = new StackPanel { Spacing = 0 };
        var attach = DialogStyles.FooterButton("Attach", DialogTone.Primary, "composer.files.attach", () => dialog.Complete(picker.Picked));
        var cancel = DialogStyles.FooterButton("Cancel", DialogTone.Plain, "composer.files.cancel", () => dialog.Complete(null));
        var body = new StackPanel
        {
            Spacing = 10,
            MinWidth = 440,
            Tag = search,
            Children = { search, crumbs, ListScroller(list), DialogStyles.Footer(cancel, attach) },
        };

        search.TextChanged += (_, _) =>
        {
            if (string.Equals(search.Text, picker.SearchText, StringComparison.Ordinal))
            {
                return;
            }

            picker.SearchText = search.Text;
            RefreshAll(search, crumbs, list, attach, picker);
        };
        search.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                dialog.Complete(null);
            }
            else if (e.Key == global::Windows.System.VirtualKey.Enter && picker.CanAttach)
            {
                e.Handled = true;
                dialog.Complete(picker.Picked);
            }
        };

        _ = ReloadAsync(picker, search, crumbs, list, attach);
        RefreshAll(search, crumbs, list, attach, picker);
        return body;
    }

    private static TextBox SearchBox()
    {
        var search = new TextBox
        {
            PlaceholderText = "Search this folder",
            FontSize = 14,
            Padding = new Thickness(10, 6, 10, 6),
        };
        AutomationProperties.SetAutomationId(search, "composer.files.search");
        AutomationProperties.SetName(search, "Search this folder");
        return search;
    }

    private static ScrollViewer ListScroller(StackPanel list)
    {
        var scroller = new ScrollViewer
        {
            Content = list,
            Height = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        AutomationProperties.SetAutomationId(scroller, "composer.files.list");
        return scroller;
    }

    private static async Task ReloadAsync(ComposerFilesPicker picker, TextBox search, StackPanel crumbs, StackPanel list, Button attach)
    {
        await picker.LoadAsync(CancellationToken.None);
        RefreshAll(search, crumbs, list, attach, picker);
    }

    private static void RefreshAll(TextBox search, StackPanel crumbs, StackPanel list, Button attach, ComposerFilesPicker picker)
    {
        RefreshCrumbs(crumbs, picker, () => RefreshAll(search, crumbs, list, attach, picker));
        if (!string.Equals(search.Text, picker.SearchText, StringComparison.Ordinal))
        {
            search.Text = picker.SearchText;
        }

        RefreshList(list, picker, () => ReloadAsync(picker, search, crumbs, list, attach), () => RefreshAll(search, crumbs, list, attach, picker));
        attach.Content = picker.AttachLabel;
        attach.IsEnabled = picker.CanAttach;
    }

    // "My files", then one crumb per folder opened; a click goes back to that level (file-picker-dialog.tsx:270-297)
    private static void RefreshCrumbs(StackPanel crumbs, ComposerFilesPicker picker, Action refresh)
    {
        crumbs.Children.Clear();
        crumbs.Visibility = picker.Searching ? Visibility.Collapsed : Visibility.Visible;
        crumbs.Children.Add(Crumb("My files", picker.AtRoot, () =>
        {
            picker.OpenRoot();
            refresh();
        }));
        for (var index = 0; index < picker.Trail.Count; index++)
        {
            var step = index;
            crumbs.Children.Add(new Barbican.App.Controls.SIcon { Kind = "ChevronRight", Size = 12, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center });
            crumbs.Children.Add(Crumb(picker.Trail[index].Name, index == picker.Trail.Count - 1, () =>
            {
                picker.GoToCrumb(step);
                refresh();
            }));
        }
    }

    private static Button Crumb(string name, bool current, Action click)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.MenuItem"],
            Content = new TextBlock
            {
                Text = name,
                FontSize = 13,
                FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = AppResources.Brush(current ? "Text" : "Dim"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Padding = new Thickness(6, 2, 6, 2),
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => click();
        return button;
    }

    // the level (folders first), the search hits, the empty line, the failure line with Retry, or a spinner while the tree loads
    private static void RefreshList(StackPanel list, ComposerFilesPicker picker, Func<Task> load, Action refresh)
    {
        list.Children.Clear();
        if (picker.LoadFailed)
        {
            var panel = new StackPanel { Spacing = 8, Padding = new Thickness(0, 40, 0, 40), HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock
            {
                Text = "Your files could not be loaded",
                FontSize = 14,
                Foreground = AppResources.Brush("Dim"),
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            panel.Children.Add(DialogStyles.FooterButton("Retry", DialogTone.Plain, "composer.files.retry", () => _ = load()));
            list.Children.Add(panel);
            return;
        }

        if (picker.IsLoading)
        {
            list.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24, Margin = new Thickness(0, 40, 0, 40), HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        if (picker.Rows.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = picker.EmptyText,
                FontSize = 14,
                Foreground = AppResources.Brush("Dim"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 40, 0, 40),
            });
            return;
        }

        for (var index = 0; index < picker.Rows.Count; index++)
        {
            var row = picker.Rows[index];
            list.Children.Add(row.IsFolder ? FolderRow(row, index, picker, refresh) : FileRow(row, index, picker, refresh));
        }
    }

    private static Button FolderRow(ComposerFilesRow row, int index, ComposerFilesPicker picker, Action refresh)
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new Barbican.App.Controls.SIcon { Kind = "Folder", Size = 18, Foreground = AppResources.Brush("Accent"), VerticalAlignment = VerticalAlignment.Center });
        var text = new TextBlock { Text = row.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var chevron = new Barbican.App.Controls.SIcon { Kind = "ChevronRight", Size = 14, Foreground = AppResources.Brush("Faint"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        content.Children.Add(chevron);

        var button = new Button
        {
            Style = (Style)Application.Current.Resources["Button.MenuItem"],
            Content = content,
            Padding = new Thickness(10, 8, 10, 8),
            MinHeight = 38,
        };
        AutomationProperties.SetAutomationId(button, $"composer.files.folder.{index}");
        AutomationProperties.SetName(button, row.Name);
        button.Click += (_, _) =>
        {
            picker.OpenFolder(row.Key, row.Name);
            refresh();
        };
        return button;
    }

    private static CheckBox FileRow(ComposerFilesRow row, int index, ComposerFilesPicker picker, Action refresh)
    {
        var content = new Grid { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = row.Name, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(row.Detail))
        {
            names.Children.Add(new TextBlock { Text = row.Detail, FontSize = 12, Foreground = AppResources.Brush("Dim"), TextTrimming = TextTrimming.CharacterEllipsis });
        }

        content.Children.Add(names);
        var size = new TextBlock
        {
            Text = Barbican.Presentation.Compose.AttachmentItem.FormatSize(row.Size),
            FontSize = 12,
            Foreground = AppResources.Brush("Dim"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(size, 1);
        content.Children.Add(size);

        // set before the handlers so a rebuild does not fire them
        var check = new CheckBox
        {
            IsChecked = picker.IsSelected(row.Key),
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 38,
            Padding = new Thickness(10, 6, 10, 6),
        };
        AutomationProperties.SetAutomationId(check, $"composer.files.file.{index}");
        AutomationProperties.SetName(check, row.Name);
        check.Checked += (_, _) =>
        {
            picker.Select(row.Key, true);
            refresh();
        };
        check.Unchecked += (_, _) =>
        {
            picker.Select(row.Key, false);
            refresh();
        };
        return check;
    }
}
