// Mirrors: Rust shortcuts.rs show_help :395 (the ? sheet: tabs Mail, Calendar, Contacts, Files; single-key rows greyed while single-key shortcuts are off); Bulwark
// components/keyboard-shortcuts-modal.tsx. Owner menus.

using Barbican.Presentation.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Palette;

/// <summary>The state of the ? shortcuts sheet.</summary>
public sealed class ShortcutSheetController : ObservableObject, IModalSurface
{
    private bool _isOpen;
    private int _tab;
    private bool _singleKeysOff;

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public int Tab => _tab;

    public static IReadOnlyList<string> Tabs => ShortcutTables.Tabs;

    /// <summary>Single-key shortcuts are off: the single-key rows are greyed.</summary>
    public bool SingleKeysOff => _singleKeysOff;

    /// <summary>The rows of the shown tab.</summary>
    public IReadOnlyList<HelpRow> Rows => ShortcutTables.HelpRows(_tab);

    /// <summary>A row whose keys are all single keys while those are off.</summary>
    public bool IsOff(HelpRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _singleKeysOff && row.Keys.Count > 0 && row.Keys.All(ShortcutTables.IsSingleKey);
    }

    public void Open(int tab, bool singleKeys)
    {
        _tab = Math.Clamp(tab, 0, Tabs.Count - 1);
        _singleKeysOff = !singleKeys;
        IsOpen = true;
        OnPropertyChanged(nameof(Tab));
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(SingleKeysOff));
    }

    public void SelectTab(int tab)
    {
        _tab = Math.Clamp(tab, 0, Tabs.Count - 1);
        OnPropertyChanged(nameof(Tab));
        OnPropertyChanged(nameof(Rows));
    }

    public void Close() => IsOpen = false;

    /// <inheritdoc />
    public bool HandleKey(KeyChord chord)
    {
        if (!IsOpen)
        {
            return false;
        }

        switch (chord.Key.ToLowerInvariant())
        {
            case "escape" or "esc" or "?":
                Close();
                break;
            case "arrowright" or "right" or "tab":
                SelectTab((_tab + 1) % Tabs.Count);
                break;
            case "arrowleft" or "left":
                SelectTab((_tab + Tabs.Count - 1) % Tabs.Count);
                break;
            default:
                break;
        }

        return true;
    }
}
