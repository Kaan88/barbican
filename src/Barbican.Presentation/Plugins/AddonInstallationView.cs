using System.Collections.Immutable;
using System.Text.Json;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;

namespace Barbican.Presentation.Plugins;

/// <summary>A display snapshot only; neither enabled state nor grants establish live execution authority.</summary>
public sealed class AddonInstallationView
{
    private AddonInstallationView(PluginInstallation installation)
    {
        var package = installation.Package;
        if (installation.Grant is not null && installation.Grant.Package != package.Identity)
            throw new ArgumentException("The grant belongs to a different package.", nameof(installation));

        Identity = package.Identity;
        Name = DisplayName(package);
        RuntimeKind = package.RuntimeKind;
        Source = package.Source;
        RequestedTier = package.RequestedTier;
        Enabled = installation.Enabled;
        Error = installation.Error;
        GrantEnabled = installation.Grant?.Enabled;
        GrantedTier = installation.Grant?.Tier;
        RequestedPermissions = Sort(package.RequestedPermissions);
        GrantedPermissions = Sort(installation.Grant?.Permissions ?? []);
    }

    public PluginPackageIdentity Identity { get; }
    public string Name { get; }
    public PluginRuntimeKind RuntimeKind { get; }
    public PluginPackageSource Source { get; }
    public PluginExecutionTier RequestedTier { get; }
    public bool Enabled { get; }
    public string? Error { get; }
    public bool? GrantEnabled { get; }
    public PluginExecutionTier? GrantedTier { get; }
    public ImmutableArray<string> RequestedPermissions { get; }
    public ImmutableArray<string> GrantedPermissions { get; }

    public static AddonInstallationView From(PluginInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        return new AddonInstallationView(installation);
    }

    internal static ImmutableArray<string> Sort(IEnumerable<string> values) =>
        [.. values.Order(StringComparer.Ordinal)];

    internal static string DisplayName(PluginPackageDefinition package) =>
        package.Manifest.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(name.GetString()) ? name.GetString()! : package.Identity.Id;
}
