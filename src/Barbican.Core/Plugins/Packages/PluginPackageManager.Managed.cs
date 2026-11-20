// Bulwark stores/plugin-store.ts managed sync and consent, with a host-verified provider route.
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Plugins.Packages.Inspection;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Plugins.Permissions;

namespace Barbican.Core.Plugins.Packages;

public sealed partial class PluginPackageManager
{
    internal sealed record ManagedApproval(string Origin, string AccountKey, string SignerSha256, string BundleSha256);

    /// <summary>Stages a managed package after the user has separately approved the provider, key and exact OAuth login.</summary>
    public async Task<Result<PluginInstallPreview>> ReviewManagedAsync(ManagedPluginSource source, AccountSessionSnapshot selected,
        Uri origin, string pinnedKey, string pluginId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!HasAuthority) return Failure<PluginInstallPreview>("Plugin profile authority is unavailable.");
        var acquired = await source.AcquireForReviewAsync(selected, origin, pinnedKey, pluginId,
            _options.BulwarkCompatibilityVersion, RunManagedWhileHeld, ct).ConfigureAwait(false);
        return acquired.IsOk
            ? await ReviewManagedCandidateAsync(acquired.Value!, ct).ConfigureAwait(false)
            : Result<PluginInstallPreview>.Fail(acquired.Error!);
    }

    private bool RunManagedWhileHeld(Func<bool> operation)
    {
        lock (_issuanceGate)
            return HasAuthority && (_lease is { } lease ? lease.TryRunWhileHeld(operation) : operation());
    }

    internal async Task<Result<PluginInstallPreview>> ReviewManagedCandidateAsync(ManagedPluginCandidate candidate, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var data = candidate.Artifact.Inspection;
        var definition = Definition(data, PluginPackageSource.Managed);
        if (!ValidManaged(Approval(candidate), data) || definition.RequestedTier != PluginExecutionTier.Privileged)
            return Failure<PluginInstallPreview>("Managed add-on approval is invalid.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<PluginInstallPreview>.Fail(loaded.Error!);
            var entry = loaded.Value!.Index.Entries.Find(item => string.Equals(item.Id, data.Id, StringComparison.Ordinal));
            if (entry is not null && !SameManagedOwner(entry.Managed, Approval(candidate)))
                return Failure<PluginInstallPreview>("An add-on with this ID is installed under different authority.");
            loaded.Value.Installations.TryGetValue(data.Id, out var previous);
            var review = Failure<PluginInstallPreview>("Managed provider approval is no longer current.");
            _ = candidate.TryRunWhileCurrent(() =>
            {
                if (ct.IsCancellationRequested || !HasAuthority) return false;
                review = Stage(data, candidate.Artifact.Archive.ToArray(), definition, previous, candidate);
                return true;
            });
            return review;
        }
        finally { _gate.Release(); }
    }

    private static ManagedApproval Approval(ManagedPluginCandidate candidate) => new(candidate.Origin.AbsoluteUri,
        candidate.LocalAccountKey, candidate.Artifact.SignerSha256, candidate.Artifact.Inspection.BundleHash);

    private static bool SameManagedOwner(ManagedApproval? first, ManagedApproval second) => first is not null &&
        string.Equals(first.Origin, second.Origin, StringComparison.Ordinal) &&
        string.Equals(first.AccountKey, second.AccountKey, StringComparison.Ordinal) &&
        string.Equals(first.SignerSha256, second.SignerSha256, StringComparison.Ordinal);

    private static bool ValidManaged(ManagedApproval approval, InspectedPluginPackage inspected) =>
        inspected.RuntimeKind == PluginRuntimeKind.JavaScript && inspected.Permissions.Contains("crypto:full", StringComparer.Ordinal) &&
        Uri.TryCreate(approval.Origin, UriKind.Absolute, out var origin) && OrganizationPolicyService.IsProviderOrigin(origin) &&
        string.Equals(origin.AbsoluteUri, approval.Origin, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(approval.AccountKey) && IsDigest(approval.SignerSha256) &&
        IsDigest(approval.BundleSha256) && string.Equals(approval.BundleSha256, inspected.BundleHash, StringComparison.Ordinal);

    private static bool IsDigest(string? hash) => hash is { Length: 64 } &&
        hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ValidationError? ManagedApprovalError(Review review, PluginInstallDecision decision)
    {
        if (review.Managed is not { } managed || !decision.ApprovePrivilegedJavaScript || decision.ApproveNativeFullTrust ||
            decision.AccountKeys.Count != 1 || !decision.AccountKeys.Contains(managed.LocalAccountKey))
            return new ValidationError("Managed add-ons require explicit privileged approval for their verified login.");
        return managed.TryRunWhileCurrent(static () => true) ? null : new ValidationError("Managed provider approval is no longer current.");
    }
}
