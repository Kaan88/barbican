// Mirrors: Bulwark install consent, strengthened by M6b hash-bound review. Host UI only; never deserialize approval from RPC.
using System.Collections.Immutable;
namespace Barbican.Core.Plugins.Packages;
public sealed class PluginInstallDecision
{
    public PluginInstallDecision(string reviewId, PluginPackageIdentity candidate, IEnumerable<string> approvedPermissions,
        IEnumerable<string> accountKeys, bool approveNativeFullTrust = false, bool approvePrivilegedJavaScript = false)
        : this(reviewId, candidate, approvedPermissions, accountKeys, null, approveNativeFullTrust, approvePrivilegedJavaScript)
    {
    }

    public PluginInstallDecision(string reviewId, PluginPackageIdentity candidate, IEnumerable<string> approvedPermissions,
        IEnumerable<string> accountKeys, IEnumerable<string>? approvedHttpOrigins,
        bool approveNativeFullTrust = false, bool approvePrivilegedJavaScript = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewId);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(approvedPermissions);
        ArgumentNullException.ThrowIfNull(accountKeys);
        ReviewId = reviewId;
        Candidate = candidate;
        ApprovedPermissions = approvedPermissions.ToImmutableHashSet(StringComparer.Ordinal);
        AccountKeys = accountKeys.ToImmutableHashSet(StringComparer.Ordinal);
        ApproveNativeFullTrust = approveNativeFullTrust;
        ApprovePrivilegedJavaScript = approvePrivilegedJavaScript;
        ApprovedHttpOrigins = (approvedHttpOrigins ?? []).ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }
    public string ReviewId { get; }
    public PluginPackageIdentity Candidate { get; }
    public ImmutableHashSet<string> ApprovedPermissions { get; }
    public ImmutableHashSet<string> AccountKeys { get; }
    public bool ApproveNativeFullTrust { get; }
    public bool ApprovePrivilegedJavaScript { get; }
    public ImmutableHashSet<string> ApprovedHttpOrigins { get; }
}
