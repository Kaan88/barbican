// Mirrors: Bulwark host-api.ts:2018-2022 publicConfig when the server has no answer ({}): the stand-in until organization administration (M7) can hold add-on settings.
using System.Text.Json.Nodes;

namespace Barbican.Core.Plugins.Api.AccountSettings;

/// <summary>A source with no administrator settings: every add-on reads an empty object.</summary>
public sealed class PluginPublicConfigNone : IPluginPublicConfig
{
    public Task<IReadOnlyDictionary<string, JsonNode?>> ReadAsync(string pluginId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<string, JsonNode?>>(new Dictionary<string, JsonNode?>(StringComparer.Ordinal));
}
