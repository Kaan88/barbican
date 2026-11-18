// Mirrors: Bulwark components/contacts/group-form.tsx (the member picker: a checkbox per contact). Owner con-ui.

using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Contacts;

/// <summary>One contact in a picker: its id, name, and whether it is ticked.</summary>
public sealed class ContactsPick(string id, string name, string? email, bool selected) : ObservableObject
{
    private bool _selected = selected;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string? Email { get; } = email;

    public bool Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }
}
