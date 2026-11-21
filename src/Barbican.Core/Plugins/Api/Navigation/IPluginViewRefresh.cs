// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2118 (ui.refreshRowBadges -> row-badges.ts:34 refreshRowBadges), :2310-2325 (ui.rerenderEmail, ui.rerenderFetchedEmails: the app re-runs onRenderEmailBody for the open
// message and onEmailsFetched for the shown list). Host seam: the views the add-on's hooks feed. An implementation re-asks the hooks on the UI thread; it never changes what the user has selected.
namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>Asks the mail views to run their add-on hooks again.</summary>
public interface IPluginViewRefresh
{
    /// <summary>Re-runs onEmailListItemRender for the rows on screen.</summary>
    Task RefreshRowBadgesAsync(CancellationToken ct);

    /// <summary>Re-runs onRenderEmailBody for the open message.</summary>
    Task RerenderEmailAsync(CancellationToken ct);

    /// <summary>Re-runs onEmailsFetched for the shown message list.</summary>
    Task RerenderFetchedEmailsAsync(CancellationToken ct);
}
