// Mirrors: M6 plan section 3.2 (set-ui builds the pages it owns; the rest are the generic rows of the catalog, D-M6-B02). Which page is hand-built: Account, Password and sign-in, the add-on page (the AI assistant add-on, D-M6b-08), Backup and import; the others are rows.

using Barbican.Presentation.Settings;

namespace Barbican.App.Views.Settings;

/// <summary>The factories of the pages set-ui draws.</summary>
public static class SetUiPages
{
    /// <summary>The factory of a page set-ui owns (a hand-built page, else the generic rows).</summary>
    internal static SetUiPageFactory For(string pageId) => pageId switch
    {
        SettingsPageIds.Account => SetUiAccountPage.Create(),
        SettingsPageIds.Security => SetUiSecurityPage.Create(),
        SettingsPageIds.Assistant => SetUiAddonPage.Create(SettingsPageIds.Assistant, SettingsPageIds.Assistant),
        SettingsPageIds.Backup => SetUiBackupPage.Create(),
        _ => Generic(pageId),
    };

    /// <summary>A page that is only its catalog rows.</summary>
    internal static SetUiPageFactory Generic(string pageId) => new(
        pageId,
        f =>
        {
            var panel = SetUiUi.Page(SettingsNav.Find(pageId)?.Title ?? pageId);
            f.Rows = new SetUiRowsView(pageId);
            panel.Children.Add(f.Rows);
            return SetUiUi.Scroll(panel);
        },
        () => SetUiUi.Run(() => SetUiUi.Model.RefreshInfoAsync(pageId, CancellationToken.None)));
}
