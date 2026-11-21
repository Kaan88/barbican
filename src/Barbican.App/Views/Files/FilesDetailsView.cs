// Mirrors: Bulwark components/files/file-details-panel.tsx (G-014): :88-93 (header h-14 box-content, px-4, border-b, title text-sm semibold, close button), :111-125 (a muted 4:3 area with the icon, the name in medium weight, the kind line, dl rows text-sm); name, kind, size, modified, location of the selection; a count when several are selected. WP-14.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.App.Views.Calendar;
using Microsoft.UI.Text;
using Barbican.Presentation.Files;

namespace Barbican.App.Views.Files;

/// <summary>The details panel.</summary>
public sealed class FilesDetailsView : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StyleState _look = ViewHost.Get<StyleState>();
    private readonly StackPanel _stack = new() { Spacing = 16, Margin = new Thickness(16) };
    private readonly Border _head = new() { Height = 57, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 0, 8, 0) };

    public FilesDetailsView()
    {
        Width = 280;
        var column = new Grid();
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.Children.Add(_head);
        var scroll = new ScrollViewer { Content = _stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        column.Children.Add(scroll);
        Content = column;
        _look.PropertyChanged += (_, _) => Render();
        AutomationProperties.SetAutomationId(this, "files.details");
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FilesViewModel.SelectedKeys) or nameof(FilesViewModel.Entries))
            {
                Render();
            }
        };
        Render();
    }

    /// <summary>Redraws from the selection.</summary>
    public void Render()
    {
        _stack.Children.Clear();
        Header();
        var sel = FilesUi.Actions.Selected;
        if (sel.Count == 0)
        {
            _stack.Children.Add(Preview("Folder", true));
            _stack.Children.Add(CalendarVisuals.Text("Select a file or folder to see its details.", 14, null, CalendarVisuals.Palette("Dim"), false));
            return;
        }

        if (sel.Count > 1)
        {
            _stack.Children.Add(Preview("File", false));
            _stack.Children.Add(CalendarVisuals.Text($"{sel.Count} selected", 14, FontWeights.Medium, null, false));
            _stack.Children.Add(Pair("Total size", FilesUi.Size(sel.Where(e => !e.IsDirectory).Sum(e => e.Size))));
            return;
        }

        var e = sel[0];
        _stack.Children.Add(Preview(e.IsDirectory ? "Folder" : "File", e.IsDirectory));
        _stack.Children.Add(CalendarVisuals.Text(e.Name, 14, FontWeights.Medium, null, false));
        _stack.Children.Add(Pair("Kind", e.IsDirectory ? "Folder" : e.MediaType ?? "File"));
        if (!e.IsDirectory)
        {
            _stack.Children.Add(Pair("Size", FilesUi.Size(e.Size)));
        }

        _stack.Children.Add(Pair("Modified", FilesUi.When(e.Modified)));
        _stack.Children.Add(Pair("Location", e.Shared ? "Shared with me" : FilesUi.Actions.Find(e.ParentKey ?? string.Empty)?.Name ?? "My files"));
    }

    // file-details-panel.tsx:88-93
    private void Header()
    {
        _head.BorderBrush = FilesVisuals.Res("Line");
        var close = FilesVisuals.IconButton(_look, "Close", "files.details.close", "Close details", () => FilesUi.RunCommand(FilesCommandIds.Details));
        close.Width = 28;
        close.Height = 28;
        var bar = new Grid();
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(new TextBlock { Text = "Details", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);
        _head.Child = bar;
    }

    // a muted 4:3 area with the icon (file-details-panel.tsx:111-113)
    private static Border Preview(string icon, bool accent)
    {
        var glyph = CalendarVisuals.Icon(icon, 56, 1.4);
        glyph.Foreground = FilesVisuals.Res(accent ? "Accent" : "Dim");
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        return new Border { Height = 150, CornerRadius = new CornerRadius(8), Background = FilesVisuals.Res("FeedbackHover"), Child = glyph };
    }

    private static StackPanel Pair(string label, string value) =>
        new() { Spacing = 1, Children = { CalendarVisuals.Text(label, 12, null, CalendarVisuals.Palette("Dim")), CalendarVisuals.Text(value, 14, null, null, false) } };
}
