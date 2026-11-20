// Mirrors Bulwark app/api/plugins/route.ts. This is inert registry metadata, not authority.
using System.Collections.Immutable;
using System.Text.Json;

namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>A validated registry row; host authentication and signature verification are separate.</summary>
public sealed record ManagedPluginRegistryEntry(string Id, string Name, string Version, string Author, string Description,
    string Type, string Entrypoint, string BundleHash, string Tier, ImmutableArray<string> Permissions,
    ImmutableArray<string> HttpOrigins, ImmutableArray<string> ApiPostPaths, JsonElement? Credentials,
    JsonElement? SettingsSchema, JsonElement? Locales, bool ForceEnabled, bool DefaultOn);
