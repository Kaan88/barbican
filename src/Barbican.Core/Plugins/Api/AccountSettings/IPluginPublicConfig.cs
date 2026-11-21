// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:2018-2022 publicConfig (the administrator's settings for one plugin that are not secrets: GET /api/admin/plugins/<id>/config?public=1, an empty object when the server does not answer).
namespace Barbican.Core.Plugins.Api.AccountSettings;

/// <summary>The administrator's non-secret settings for add-ons. Barbican has no administrator configuration store yet (organization administration is M7), so the default source (<see cref="PluginPublicConfigNone"/>) holds none.</summary>
public interface IPluginPublicConfig
{
    /// <summary>The non-secret settings of the add-on <paramref name="pluginId"/> and of no other, as JSON values by name; empty when there are none.</summary>
    Task<IReadOnlyDictionary<string, System.Text.Json.Nodes.JsonNode?>> ReadAsync(string pluginId, CancellationToken ct);
}
