// Mirrors: Bulwark components/settings/keyword-settings.tsx (tag manager: add, rename, colour, delete; nested tags with "/"), Rust tags.rs; I-431. The tag definitions live in the settings (emailKeywords) and TagService saves them (M2 stream actions); messages keep their keyword when a definition is deleted, and the confirmation says so.
// TagService is synchronous over the prefs editor, so this model is too. Owner srv-ui.

using Barbican.Core.Common;
using Barbican.Core.Mail.Tags;
using Barbican.Presentation.Tags;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The tags half of the Organize page.</summary>
public sealed class SrvUiTagsViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private readonly TagService _tags;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    public SrvUiTagsViewModel(TagService tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags = tags;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string? Error { get; private set; }

    public string? SelectedId { get; private set; }

    public IReadOnlyList<TagRow> Rows => TagRules.Rows(_tags.Defs, nested: true, _collapsed);

    public static IReadOnlyList<string> Palette => TagRules.Palette;

    public void Select(string? id)
    {
        SelectedId = id;
        Raise();
    }

    public void ToggleCollapsed(string id)
    {
        if (!_collapsed.Remove(id))
        {
            _ = _collapsed.Add(id);
        }

        Raise();
    }

    /// <summary>Adds a tag (under the selected one when <paramref name="underSelected"/>).</summary>
    public bool Add(string name, bool underSelected)
    {
        var result = _tags.Add(underSelected ? SelectedId : null, name);
        if (result.IsOk)
        {
            SelectedId = result.Value;
        }

        return Done(result);
    }

    public bool Rename(string name) => SelectedId is not null && Done(_tags.Rename(SelectedId, name));

    public bool SetColour(string colour) => SelectedId is not null && Done(_tags.SetColour(SelectedId, colour));

    /// <summary>Deletes the selected definition; the page asked first.</summary>
    public bool Delete()
    {
        if (SelectedId is null)
        {
            return false;
        }

        var ok = Done(_tags.Delete(SelectedId));
        if (ok)
        {
            SelectedId = null;
            Raise();
        }

        return ok;
    }

    public string? ColourOf(string id) => _tags.ColourOf(id);

    /// <summary>Re-reads the definitions (the settings may have changed elsewhere).</summary>
    public void Refresh() => Raise();

    private bool Done<T>(Result<T> result)
    {
        Error = result.IsOk ? null : result.Error!.Message;
        Raise();
        return result.IsOk;
    }

    private void Raise() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(string.Empty));
}
