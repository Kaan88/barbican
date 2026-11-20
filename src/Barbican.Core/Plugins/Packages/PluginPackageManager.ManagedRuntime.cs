// Bulwark stores/plugin-store.ts, with session-only activation for verified managed packages.
using Barbican.Core.Common;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Packages;

public sealed partial class PluginPackageManager
{
    private sealed record ManagedRuntime(PluginGrantSnapshot Grant, ManagedPluginStartupProof Proof);

    // All managed session state is protected by the issuance gate. The durable entry stays disabled.
    private readonly Dictionary<string, Guid> _managedStarts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManagedRuntime> _managedRuntimes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PluginGrantSnapshot> _managedRevoking = new(StringComparer.Ordinal);
    private ITimer? _managedTimer;

    /// <summary>Re-verifies a managed installation and authorizes it for the current provider session.</summary>
    public Task<Result<PluginInstallation>> EnableManagedForSessionAsync(PluginPackageIdentity package, ManagedPluginSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        return EnableManagedForSessionAsync(package, (approval, fence) => source.AcquireForActivationAsync(approval.AccountKey,
            new Uri(approval.Origin), approval.SignerSha256, package.Id, _options.BulwarkCompatibilityVersion, fence, ct), ct);
    }

    internal async Task<Result<PluginInstallation>> EnableManagedForSessionAsync(PluginPackageIdentity package,
        Func<ManagedApproval, Func<Func<bool>, bool>, Task<Result<ManagedPluginCandidate>>> acquire, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(package);
        var attempt = Guid.NewGuid();
        var published = false;
        try
        {
            var reserved = await ReserveManagedStartAsync(package, attempt, ct).ConfigureAwait(false);
            if (!reserved.IsOk) return Result<PluginInstallation>.Fail(reserved.Error!);
            var proof = await VerifyManagedStartupAsync(package, (approval, fence) =>
                acquire(approval, issue => fence(() => _managedStarts.GetValueOrDefault(package.Id) == attempt && issue())), ct).ConfigureAwait(false);
            if (!proof.IsOk) return Result<PluginInstallation>.Fail(proof.Error!);
            var result = await PublishManagedRuntimeAsync(proof.Value!, attempt, ct).ConfigureAwait(false);
            published = result.IsOk;
            return result;
        }
        finally
        {
            if (!published)
                lock (_issuanceGate)
                    if (_managedStarts.GetValueOrDefault(package.Id) == attempt) _managedStarts.Remove(package.Id);
        }
    }

    private async Task<Result<Unit>> ReserveManagedStartAsync(PluginPackageIdentity package, Guid attempt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<Unit>.Fail(loaded.Error!);
            if (!loaded.Value!.Installations.TryGetValue(package.Id, out var installed) ||
                installed.Package.Identity != package || installed.Package.Source != PluginPackageSource.Managed)
                return Failure<Unit>("The approved managed installation changed.");

            PluginGrantSnapshot? cleanup;
            lock (_issuanceGate)
            {
                if (!HasAuthority || _blocked.Contains(package.Id) || !_managedStarts.TryAdd(package.Id, attempt))
                    return Failure<Unit>("The managed add-on is already starting, running or stopping.");
                cleanup = _managedRevoking.GetValueOrDefault(package.Id);
            }
            return cleanup is null ? Result<Unit>.Ok(default) : await WaitForManagedStopAsync(cleanup).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<Result<PluginInstallation>> PublishManagedRuntimeAsync(ManagedPluginStartupProof proof, Guid attempt, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        var package = proof.Package;
        PluginGrantSnapshot? activated = null;
        try
        {
            var loaded = await LoadAsync(ct).ConfigureAwait(false);
            if (!loaded.IsOk) return Result<PluginInstallation>.Fail(loaded.Error!);
            var current = loaded.Value!;
            if (!current.Installations.TryGetValue(package.Id, out var installation) || installation.Package.Identity != package ||
                installation.Grant is not { Tier: PluginExecutionTier.Privileged } installed ||
                installed.Generation != proof.InstalledGeneration || _blocked.Contains(package.Id))
                return Failure<PluginInstallation>("The approved managed installation changed.");

            // Reserve a unique generation while leaving the on-disk installation disabled.
            var generation = current.Index.NextGeneration;
            var saved = await CommitAsync(current, current.Index with { NextGeneration = generation + 1 }, proof.TryRunWhileCurrent, ct).ConfigureAwait(false);
            if (!saved.IsOk) return Result<PluginInstallation>.Fail(saved.Error!);
            var grant = new PluginGrantSnapshot(package, generation, true, installed.Permissions, installed.AccountKeys,
                installed.ApprovedHttpOrigins, installed.Tier);
            return proof.TryRunWhileCurrent(() => PublishManagedGrant(grant, proof, attempt, ct))
                ? Result<PluginInstallation>.Ok(installation with { Enabled = true, Grant = activated = grant })
                : Failure<PluginInstallation>("The managed add-on approval is no longer current.");
        }
        finally
        {
            _gate.Release();
            if (activated is not null) Changed?.Invoke(this, new(package.Id, activated.Generation));
        }
    }

    private bool PublishManagedGrant(PluginGrantSnapshot grant, ManagedPluginStartupProof proof, Guid attempt, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _managedRevoking.ContainsKey(grant.Package.Id) ||
            _managedStarts.GetValueOrDefault(grant.Package.Id) != attempt) return false;
        _managedTimer ??= _time.CreateTimer(static state => ((PluginPackageManager)state!).ExpireManagedRuntimes(), this,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _managedRuntimes.Add(grant.Package.Id, new(grant, proof));
        _issued[grant.Package] = Math.Max(_issued.GetValueOrDefault(grant.Package), grant.Generation);
        return true;
    }

    /// <summary>Gets the startup proof tied to the current host-issued runtime generation.</summary>
    public Task<Result<ManagedPluginStartupProof>> GetManagedRuntimeProofAsync(PluginRuntimeBinding binding, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ct.ThrowIfCancellationRequested();
        lock (_issuanceGate)
        {
            return Task.FromResult(TryUseManagedRuntime(binding, static () => true)
                ? Result<ManagedPluginStartupProof>.Ok(_managedRuntimes[binding.Package.Id].Proof)
                : Failure<ManagedPluginStartupProof>("The managed runtime approval is no longer current."));
        }
    }

    private PluginInstallation ProjectManagedRuntime(PluginInstallation installation)
    {
        if (installation.Package.Source != PluginPackageSource.Managed) return installation;
        lock (_issuanceGate)
        {
            return _managedRuntimes.TryGetValue(installation.Package.Identity.Id, out var runtime) &&
                runtime.Grant.Package == installation.Package.Identity && runtime.Proof.TryRunWhileCurrent(static () => true)
                ? installation with { Enabled = true, Grant = runtime.Grant }
                : installation;
        }
    }

    private bool TryUseManagedRuntime(PluginRuntimeBinding binding, Func<bool> issue) =>
        HasAuthority && binding.IsAlive && binding.Tier == PluginExecutionTier.Privileged &&
        string.Equals(binding.Profile, _options.Profile, StringComparison.Ordinal) &&
        _managedRuntimes.TryGetValue(binding.Package.Id, out var runtime) && runtime.Grant.Package == binding.Package &&
        runtime.Grant.Generation == binding.GrantGeneration && runtime.Proof.TryRunWhileCurrent(issue);

    private bool TryRunWithManagedBindings(PluginRuntimeBinding[] managed, int index, Func<bool> issue) =>
        index == managed.Length ? issue() : TryUseManagedRuntime(managed[index], () => TryRunWithManagedBindings(managed, index + 1, issue));

    private PluginGrantSnapshot? WithdrawManagedRuntime(string id, long? expectedGeneration = null)
    {
        if (expectedGeneration is { } expected &&
            (!_managedRuntimes.TryGetValue(id, out var current) || current.Grant.Generation != expected)) return null;
        _managedStarts.Remove(id);
        if (_managedRuntimes.Remove(id, out var runtime)) _managedRevoking[id] = runtime.Grant;
        return _managedRevoking.GetValueOrDefault(id);
    }

    /// <summary>Rolls back a failed startup without touching a newer session grant.</summary>
    public Task<Result<Unit>> RollBackManagedRuntimeAsync(PluginGrantSnapshot grant, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grant);
        PluginGrantSnapshot? withdrawn;
        lock (_issuanceGate) withdrawn = WithdrawManagedRuntime(grant.Package.Id, grant.Generation);
        return withdrawn is null ? Task.FromResult(Result<Unit>.Ok(default)) : FinishManagedStopAsync(withdrawn, ct);
    }

    private Task<Result<Unit>> StopManagedRuntimeAsync(string id)
    {
        PluginGrantSnapshot? withdrawn;
        lock (_issuanceGate) withdrawn = WithdrawManagedRuntime(id);
        if (_blocked.Contains(id)) return Task.FromResult(Failure<Unit>("The managed add-on is already stopping."));
        return withdrawn is null ? Task.FromResult(Result<Unit>.Ok(default)) : WaitForManagedStopAsync(withdrawn);
    }

    private async Task<Result<Unit>> WaitForManagedStopAsync(PluginGrantSnapshot grant)
    {
        _blocked.Add(grant.Package.Id);
        _gate.Release();
        try { return await FinishManagedStopAsync(grant, CancellationToken.None).ConfigureAwait(false); }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _blocked.Remove(grant.Package.Id);
        }
    }

    private async Task<Result<Unit>> FinishManagedStopAsync(PluginGrantSnapshot grant, CancellationToken ct)
    {
        var result = await _revocation.RevokeAsync(grant.Package, grant.Generation, ct).ConfigureAwait(false);
        lock (_issuanceGate)
            if (result.IsOk && ReferenceEquals(_managedRevoking.GetValueOrDefault(grant.Package.Id), grant))
                _managedRevoking.Remove(grant.Package.Id);
        Changed?.Invoke(this, new(grant.Package.Id, grant.Generation));
        return result;
    }

    private void ExpireManagedRuntimes()
    {
        PluginGrantSnapshot[] expired;
        lock (_issuanceGate)
        {
            if (!HasAuthority) return;
            expired = [.. _managedRuntimes.Values.Where(runtime => !runtime.Proof.TryRunWhileCurrent(static () => true)).Select(runtime => runtime.Grant)];
            foreach (var grant in expired) _ = WithdrawManagedRuntime(grant.Package.Id, grant.Generation);
        }
        foreach (var grant in expired) _ = FinishManagedStopAsync(grant, CancellationToken.None);
    }
}
