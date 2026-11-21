// Mirrors: Bulwark RegisteredPage.pluginName (pages.ts:14-17, host-api.ts:2123 passes plugin.name). Barbican keeps the manifest's display name of every add-on whose runtime it started.
using System.Collections.Concurrent;
using System.Text.Json;

namespace Barbican.Core.Plugins.Pages;

/// <summary>Display names of installed add-ons by package id, for labels that name the add-on (a page's tooltip).</summary>
public sealed class PluginAddOnNames
{
    private const int MaxLength = 80;
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.Ordinal);

    /// <summary>Keeps the manifest's <c>name</c> (trimmed, at most 80 characters); an add-on without one is shown by its id.</summary>
    public void Remember(string packageId, JsonElement manifest)
    {
        if (manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            && name.GetString()?.Trim() is { Length: > 0 } text)
        {
            _names[packageId] = text.Length > MaxLength ? text[..MaxLength] : text;
        }
    }

    /// <summary>The add-on's display name, or its id when none is known.</summary>
    public string Resolve(string packageId) => _names.TryGetValue(packageId, out var name) ? name : packageId;
}
