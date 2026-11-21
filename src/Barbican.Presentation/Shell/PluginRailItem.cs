// Mirrors: Bulwark components/layout/navigation-rail.tsx:459-512,700-720 (one rail entry per add-on page: its icon, active while its route is on screen, tooltip "title (add-on)").
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>One add-on page in the rail.</summary>
public sealed class PluginRailItem(string key, string title, string tooltip, string iconKind) : ObservableObject
{
    private bool _isActive;

    /// <summary>"package/page".</summary>
    public string Key { get; } = key;

    public string Title { get; } = title;

    public string Tooltip { get; } = tooltip;

    /// <summary>A key of the icon set.</summary>
    public string IconKind { get; } = iconKind;

    /// <summary>The add-on page is the one on screen.</summary>
    public bool IsActive
    {
        get => _isActive;
        internal set => SetProperty(ref _isActive, value);
    }
}
