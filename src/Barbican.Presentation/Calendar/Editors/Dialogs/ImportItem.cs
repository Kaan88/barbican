// One event of an .ics file in the import dialog's list. Owner cal-ui.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Calendar.Editors.Dialogs;

/// <summary>An event found in the file: its title, when it starts, and whether it will be imported.</summary>
public sealed class ImportItem(int index, string title, string when, bool selected) : ObservableObject
{
    private bool _selected = selected;

    public int Index { get; } = index;

    public string Title { get; } = title;

    public string When { get; } = when;

    public bool Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }
}
