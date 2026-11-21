// Mirrors: Bulwark lib/plugin-sandbox/page-navigation.ts:14-62 (onPageNavigation handler, requestPageNavigation resolves false when nothing can take the move).
// Host seam: the shell shows an add-on's own page. Core has already checked the grant, that the page belongs to the calling add-on and the path; an implementation only moves the UI, on the UI thread.
namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>Moves the app to an add-on's page for the add-on.</summary>
public interface IPluginPageOpener
{
    /// <summary>Shows page <paramref name="pageId"/> of the add-on <paramref name="packageId"/> with <paramref name="path"/> below it (no leading slash, may be empty); false when nothing could take the move.</summary>
    Task<bool> OpenPageAsync(string packageId, string pageId, string path, CancellationToken ct);
}
