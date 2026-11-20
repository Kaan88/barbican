// Mirrors: Bulwark package review/update flow. ReviewId refers to host-owned immutable staging, never a plugin-provided file path.
namespace Barbican.Core.Plugins.Packages;
public sealed record PluginInstallPreview(string ReviewId, PluginPackageDefinition Candidate, PluginInstallation? Previous,
    string? VerifiedSigner = null, bool ManagedApprovalVerified = false)
{
    public Uri? ManagedOrigin { get; init; }
    public string? ManagedAccountKey { get; init; }
}
