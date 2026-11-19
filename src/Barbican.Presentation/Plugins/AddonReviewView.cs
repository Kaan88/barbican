using System.Collections.Immutable;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;

namespace Barbican.Presentation.Plugins;

/// <summary>
/// Inert review presentation. Permission differences compare declarations, not grants.
/// ReviewId is an opaque correlation value, not an approval or a live authority.
/// </summary>
public sealed class AddonReviewView
{
    private AddonReviewView(PluginInstallPreview preview)
    {
        var candidate = preview.Candidate;
        var previous = preview.Previous is null ? null : AddonInstallationView.From(preview.Previous);
        if (previous is not null && !string.Equals(previous.Identity.Id, candidate.Identity.Id, StringComparison.Ordinal))
            throw new ArgumentException("The previous installation has a different plugin id.", nameof(preview));

        ReviewId = preview.ReviewId;
        CandidateIdentity = candidate.Identity;
        CandidateName = AddonInstallationView.DisplayName(candidate);
        CandidateRuntimeKind = candidate.RuntimeKind;
        CandidateSource = candidate.Source;
        CandidateRequestedTier = candidate.RequestedTier;
        Previous = previous;
        var oldPermissions = previous?.RequestedPermissions ?? [];
        var requested = candidate.RequestedPermissions;
        AddedPermissions = AddonInstallationView.Sort(requested.Except(oldPermissions, StringComparer.Ordinal));
        RemovedPermissions = AddonInstallationView.Sort(oldPermissions.Except(requested, StringComparer.Ordinal));
        UnchangedPermissions = AddonInstallationView.Sort(requested.Intersect(oldPermissions, StringComparer.Ordinal));
        PreviouslyGrantedPermissions = previous?.GrantedPermissions ?? [];
    }

    public string ReviewId { get; }
    public PluginPackageIdentity CandidateIdentity { get; }
    public string CandidateName { get; }
    public PluginRuntimeKind CandidateRuntimeKind { get; }
    public PluginPackageSource CandidateSource { get; }
    public PluginExecutionTier CandidateRequestedTier { get; }
    public AddonInstallationView? Previous { get; }
    public PluginPackageIdentity? PreviousIdentity => Previous?.Identity;
    /// <summary>True only for an update whose complete package hash differs; false for a fresh install.</summary>
    public bool ContentChanged => Previous is not null &&
        !string.Equals(Previous.Identity.Sha256, CandidateIdentity.Sha256, StringComparison.Ordinal);
    public ImmutableArray<string> AddedPermissions { get; }
    public ImmutableArray<string> RemovedPermissions { get; }
    public ImmutableArray<string> UnchangedPermissions { get; }
    public ImmutableArray<string> PreviouslyGrantedPermissions { get; }

    public static AddonReviewView From(PluginInstallPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new AddonReviewView(preview);
    }
}
