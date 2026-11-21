// Mirrors: Bulwark lib/plugin-sandbox/pages.ts:14-17 RegisteredPage (a page with its add-on's id and name).
namespace Barbican.Core.Plugins.Pages;

/// <summary>A page in the registry: the add-on's page and who owns it.</summary>
public sealed record RegisteredPluginPage(string PackageId, string PackageName, PluginPage Page)
{
    /// <summary>Stable key of the page across the app ("package/page"), the route Bulwark uses (/apps/&lt;pluginId&gt;/&lt;pageId&gt;, use-plugin-pages.ts).</summary>
    public string Key => PackageId + "/" + Page.Id;

    /// <summary>Bulwark navigation-rail.tsx:700-720: the tooltip of an icon-only rail entry is "title (add-on)".</summary>
    public string Tooltip => Page.Title + " (" + PackageName + ")";
}
