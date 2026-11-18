// Mirrors: M6 plan section 4.1. The seam of the settings screen: set-ui owns the screen (SetUiScreenFactory, the navigation, the search, the generic schema-driven pages) and builds a page from the factory registered for its id; srv-ui registers factories for its pages and never edits the screen.
// A page id with no factory shows the generic schema rows of that page (set-ui). Frozen in the M6 plan commit; owner integrator.

namespace Barbican.App.Settings;

/// <summary>The view of one settings page.</summary>
public interface ISettingsPageFactory
{
    /// <summary>The page this factory builds (a Barbican.Presentation.Settings.SettingsPageIds value).</summary>
    string PageId { get; }

    /// <summary>Builds the page once; the screen keeps it and only hides it.</summary>
    UIElement Create();

    /// <summary>Called each time the page is shown (reload what the server may have changed). Runs on the UI thread; start data loads off it.</summary>
    void OnShown();

    /// <summary>Scrolls to or highlights a row or section (an anchor from <c>ISettingsNavigation</c> or from a search hit).</summary>
    void Reveal(string anchor);
}
