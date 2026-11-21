// Mirrors: Bulwark lib/plugin-sandbox/pages.ts:7-12 PluginPage (id, title, optional host icon name).
namespace Barbican.Core.Plugins.Pages;

/// <summary>One page an add-on offers (<c>api.ui.setPages</c>): the rail entry's label and icon.</summary>
/// <param name="Id">Lowercase letters, digits and hyphens, at most 32 characters, unique within the add-on.</param>
/// <param name="Title">The label, trimmed, 1 to 40 characters.</param>
/// <param name="Icon">The name of a host icon, or null for the puzzle piece.</param>
public sealed record PluginPage(string Id, string Title, string? Icon);
