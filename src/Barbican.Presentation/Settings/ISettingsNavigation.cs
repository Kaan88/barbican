// Mirrors: Bulwark components/settings/settings-app.tsx (tab state, back and forward, cross-links between pages; I-397). The one place that says which page is open: the screen shows it, a page opens another with Open (for example Account's "Manage addresses" opens identities), the palette and commands open pages with it.
// Frozen in the M6 plan commit; owner integrator. Raised on the thread that called Open (the UI thread).

namespace Barbican.Presentation.Settings;

/// <summary>Which settings page is open.</summary>
public interface ISettingsNavigation
{
    /// <summary>The open page id (a <see cref="SettingsPageIds"/> value).</summary>
    string Current { get; }

    /// <summary>The row or section to scroll to on the open page, or null.</summary>
    string? Anchor { get; }

    bool CanGoBack { get; }

    bool CanGoForward { get; }

    /// <summary>Raised after Current or Anchor changed.</summary>
    event EventHandler? Changed;

    /// <summary>Opens a page (an unknown id is ignored); opening the page that is open only changes the anchor. Drops the forward history.</summary>
    void Open(string pageId, string? anchor = null);

    bool Back();

    bool Forward();
}
