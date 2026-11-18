// Mirrors: Bulwark components/files/file-details-panel.tsx (G-014): name, kind, size, modified, location of the selection; a count when several are selected.

using Microsoft.UI.Xaml.Automation;
using Barbican.App.Views.Calendar;
using Barbican.Presentation.Files;

namespace Barbican.App.Views.Files;

/// <summary>The details panel.</summary>
public sealed class FilesDetailsView : UserControl
{
    private readonly FilesViewModel _vm = FilesUi.Model;
    private readonly StackPanel _stack = new() { Spacing = 10, Margin = new Thickness(16) };

    public FilesDetailsView()
    {
        Width = 280;
        Content = _stack;
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
        _stack.Children.Add(CalendarVisuals.Text("Details", 15, Microsoft.UI.Text.FontWeights.SemiBold, CalendarVisuals.Palette("Text")));
        var sel = FilesUi.Actions.Selected;
        if (sel.Count == 0)
        {
            _stack.Children.Add(CalendarVisuals.Text("Select a file or folder to see its details.", 12.5, null, CalendarVisuals.Palette("Dim"), false));
            return;
        }

        if (sel.Count > 1)
        {
            _stack.Children.Add(CalendarVisuals.Text($"{sel.Count} items selected", 13, null, null, false));
            _stack.Children.Add(Pair("Total size", FilesUi.Size(sel.Where(e => !e.IsDirectory).Sum(e => e.Size))));
            return;
        }

        var e = sel[0];
        _stack.Children.Add(CalendarVisuals.Text(e.Name, 13.5, Microsoft.UI.Text.FontWeights.SemiBold, null, false));
        _stack.Children.Add(Pair("Kind", e.IsDirectory ? "Folder" : e.MediaType ?? "File"));
        if (!e.IsDirectory)
        {
            _stack.Children.Add(Pair("Size", FilesUi.Size(e.Size)));
        }

        _stack.Children.Add(Pair("Modified", FilesUi.When(e.Modified)));
        _stack.Children.Add(Pair("Location", e.Shared ? "Shared with me" : FilesUi.Actions.Find(e.ParentKey ?? string.Empty)?.Name ?? "My files"));
    }

    private static StackPanel Pair(string label, string value) =>
        new() { Spacing = 1, Children = { CalendarVisuals.Text(label, 11, null, CalendarVisuals.Palette("Faint")), CalendarVisuals.Text(value, 13, null, null, false) } };
}
