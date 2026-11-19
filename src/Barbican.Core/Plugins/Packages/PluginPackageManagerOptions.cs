// Source: Bulwark stores/plugin-store.ts:188-320; expiring, bounded hash-bound reviews are M6b host policy.
namespace Barbican.Core.Plugins.Packages;

/// <summary>Host-selected package review and staging limits, not package-supplied authority.</summary>
public sealed record PluginPackageManagerOptions(string Profile, string BulwarkCompatibilityVersion)
{
    public TimeSpan ReviewLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxPendingReviews { get; init; } = 8;
    public long MaxExpandedStagingBytes { get; init; } = 64 * 1024 * 1024;
}
