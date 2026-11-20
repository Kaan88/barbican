// Bulwark stores/plugin-store.ts managed sync and consent, with a host-verified provider route.
using System.Text;
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
    private static readonly UTF8Encoding ManagedCodeEncoding = new(false, true);
    private readonly Dictionary<string, Entry> _managedVerificationEntries = new(StringComparer.Ordinal);
    internal sealed record ManagedApproval(string Origin, string AccountKey, string SignerSha256, string BundleSha256);

    /// <summary>Re-verifies the exact installed package through the previously approved provider session.</summary>
    public Task<Result<ManagedPluginStartupProof>> VerifyManagedStartupAsync(PluginPackageIdentity package,
        ManagedPluginSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return VerifyManagedStartupAsync(package, (approval, fence) => source.AcquireForActivationAsync(approval.AccountKey,
            new Uri(approval.Origin), approval.SignerSha256, package.Id, _options.BulwarkCompatibilityVersion, fence, ct), ct);
    }

    internal async Task<Result<ManagedPluginStartupProof>> VerifyManagedStartupAsync(PluginPackageIdentity package,
        Func<ManagedApproval, Func<Func<bool>, bool>, Task<Result<ManagedPluginCandidate>>> acquire, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        var installed = await FindManagedStartupEntryAsync(package, ct).ConfigureAwait(false);
        if (!installed.IsOk) return Result<ManagedPluginStartupProof>.Fail(installed.Error!);
        var entry = installed.Value!;
        var approval = entry.Managed!;

        bool Fence(Func<bool> issue) => RunManagedWhileHeld(() =>
            !ct.IsCancellationRequested && IsCurrentManagedEntry(entry) && issue());

        var fetched = await acquire(approval, Fence).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!fetched.IsOk) return Result<ManagedPluginStartupProof>.Fail(fetched.Error!);
        var candidate = fetched.Value!;
        var inspection = candidate.Artifact.Inspection;
        if (Approval(candidate) != approval || !string.Equals(inspection.Id, package.Id, StringComparison.Ordinal) ||
            !string.Equals(inspection.Version, package.Version, StringComparison.Ordinal) ||
            !string.Equals(inspection.WholeArchiveSha256, package.Sha256, StringComparison.Ordinal) ||
            !inspection.Resources.TryGetValue(inspection.EntryPoint, out var code))
            return Failure<ManagedPluginStartupProof>("The managed package has changed. Review the add-on again.");

        bool WhileCurrent(Func<bool> issue) => RunManagedWhileHeld(() =>
            IsCurrentManagedEntry(entry) && candidate.TryRunWhileCurrent(issue));

        if (!Fence(() => candidate.TryRunWhileCurrent(static () => true)))
            return Failure<ManagedPluginStartupProof>("Managed add-on approval is no longer current.");
        return Result<ManagedPluginStartupProof>.Ok(new(package, entry.Generation, ManagedCodeEncoding.GetString(code.AsSpan()), WhileCurrent));
    }

    private async Task<Result<Entry>> FindManagedStartupEntryAsync(PluginPackageIdentity package, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Entry>.Fail(loaded.Error!);
            var entry = loaded.Value!.Index.Entries.Find(item => string.Equals(item.Id, package.Id, StringComparison.Ordinal));
            return entry is { Managed: not null, CleanupThrough: null } && Identity(entry) == package && !_blocked.Contains(package.Id)
                ? Result<Entry>.Ok(entry)
                : Failure<Entry>("The approved managed installation is unavailable or has changed.");
        }
        finally { _gate.Release(); }
    }

    // Protected by _issuanceGate, including when the native HTTP request actually starts.
    private bool IsCurrentManagedEntry(Entry entry) => _managedVerificationEntries.TryGetValue(entry.Id, out var current) &&
        SameManagedEntry(current, entry);

    private static bool SameManagedEntry(Entry? current, Entry entry) => current is { Managed: not null, CleanupThrough: null } &&
        current.Generation == entry.Generation && Identity(current) == Identity(entry) && current.Managed == entry.Managed;

    private void InvalidateChangedManagedEntries(Index previous, Index next)
    {
        lock (_issuanceGate)
            foreach (var entry in previous.Entries.Where(item => item.Managed is not null))
                if (!SameManagedEntry(next.Entries.Find(item => string.Equals(item.Id, entry.Id, StringComparison.Ordinal)), entry))
                    _managedVerificationEntries.Remove(entry.Id);
    }

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
