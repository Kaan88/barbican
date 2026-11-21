// Mirrors: Bulwark lib/plugin-sandbox/loader.ts and host-bridge.ts:327-459.
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.OAuth;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Starts an isolated JavaScript runtime under the current package grant.</summary>
public sealed class PluginSandboxFactory(IPluginPackageManager packages, IPluginAuthority authority,
    IPluginRevocationCoordinator revocation, IPluginApiDispatcher dispatcher, IPluginSandboxWebViewFactory views,
    IPluginSandboxStartup? startup = null, HookBus? hooks = null, PluginOAuthCallbacks? oauth = null) : IPluginRuntimeFactory
{
    private const int MaxEntrypointBytes = 20 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<Result<IPluginRuntimeInstance>> StartAsync(PluginRuntimeStart request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (startup is null || !ProfilePaths.IsValidName(request.Profile)) return Fail();

        try
        {
            var trusted = await CurrentAsync(request, ct).ConfigureAwait(false);
            if (trusted is null) return Fail();

            var code = trusted.Grant.Tier == PluginExecutionTier.Privileged ? null : await ReadEntrypointAsync(trusted, ct).ConfigureAwait(false);
            if ((trusted.Grant.Tier != PluginExecutionTier.Privileged && code is null) || await CurrentAsync(trusted, ct).ConfigureAwait(false) is null) return Fail();

            return await StartIsolatedAsync(startup, trusted, code, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return Fail(); }
    }

    private async Task<Result<IPluginRuntimeInstance>> StartIsolatedAsync(IPluginSandboxStartup activator,
        PluginRuntimeStart trusted, string? code, CancellationToken ct)
    {
        var binding = PluginRuntimeBinding.Create(trusted.Profile, trusted.Grant, trusted.Kind, trusted.Slot);
        PluginSandboxStartupLifetime? lifetime = null;
        var transferred = false;
        try
        {
            var view = await views.CreateAsync("plugin-" + Guid.NewGuid().ToString("N"), ct).ConfigureAwait(false);
            var session = new PluginSandboxSession(binding, view, dispatcher, authority, trusted.Callbacks);
            lifetime = new PluginSandboxStartupLifetime(session);
            if (await CurrentAsync(trusted, ct).ConfigureAwait(false) is null) return Fail();

            var tracked = revocation.Track(binding, _ => lifetime.DisposeAsync());
            if (!tracked.IsOk || tracked.Value is not { } registration) return Fail();
            lifetime.AttachRegistration(registration);

            if (!binding.IsAlive || await CurrentAsync(trusted, ct).ConfigureAwait(false) is not { } current) return Fail();
            ManagedPluginStartupProof? proof = null;
            if (current.Grant.Tier == PluginExecutionTier.Privileged)
            {
                if (packages is not PluginPackageManager manager) return Fail();
                var found = await manager.GetManagedRuntimeProofAsync(binding, ct).ConfigureAwait(false);
                if (!found.IsOk || found.Value is not { } verified || verified.Package != binding.Package) return Fail();
                proof = verified;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, binding.Lifetime);
            var instance = await ActivateAsync(activator, session, lifetime, current, code, proof, linked.Token).ConfigureAwait(false);
            if (instance is null) return Fail();

            if (await CurrentAsync(current, ct).ConfigureAwait(false) is null || !binding.IsAlive) return Fail();
            ct.ThrowIfCancellationRequested();
            transferred = true;
            return Result<IPluginRuntimeInstance>.Ok(new PluginSandboxStartupOwnedInstance(instance, lifetime));
        }
        finally
        {
            if (!transferred)
            {
                if (lifetime is null) await binding.DisposeAsync().ConfigureAwait(false);
                else await lifetime.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<IPluginRuntimeInstance?> ActivateAsync(IPluginSandboxStartup activator, PluginSandboxSession session,
        PluginSandboxStartupLifetime lifetime, PluginRuntimeStart current, string? code, ManagedPluginStartupProof? proof, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!lifetime.TryBeginActivation()) return null;

        IPluginRuntimeInstance? instance = null;
        PluginSandboxLifecycleHooks? observers = null;
        IDisposable? oauthReceiver = null;
        bool accepted;
        try
        {
            var result = proof is not null
                ? await activator.ActivateManagedAsync(session, current, proof, ct).ConfigureAwait(false)
                : code is not null ? await activator.ActivateAsync(session, current, code, ct).ConfigureAwait(false) : Fail();
            if (result.IsOk)
            {
                instance = result.Value;
                if (instance is not null && ReferenceEquals(session.Binding, instance.Binding))
                {
                    if (hooks is not null && authority is IPluginDeliveryAuthority delivery)
                        observers = await PluginSandboxLifecycleHooks.AttachAsync(hooks, instance, delivery, ct).ConfigureAwait(false);
                    oauthReceiver = oauth?.Register(instance);
                }
            }
        }
        finally { accepted = lifetime.FinishActivation(instance, observers, oauthReceiver); }

        return accepted ? instance : null;
    }

    private async Task<string?> ReadEntrypointAsync(PluginRuntimeStart request, CancellationToken ct)
    {
        var opened = await packages.OpenResourceAsync(request.Grant.Package, request.Installation.Package.Entrypoint, ct).ConfigureAwait(false);
        if (!opened.IsOk || opened.Value is null) return null;

        await using var stream = opened.Value;
        using var data = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (data.Length + count > MaxEntrypointBytes) return null;
            await data.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
        }
        return DecodeEntrypoint(data.ToArray());
    }

    private static string DecodeEntrypoint(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Encoding.UTF8.Preamble)) bytes = bytes[Encoding.UTF8.Preamble.Length..];
        return StrictUtf8.GetString(bytes);
    }

    private async Task<PluginRuntimeStart?> CurrentAsync(PluginRuntimeStart request, CancellationToken ct)
    {
        var installed = await packages.FindAsync(request.Grant.Package.Id, ct).ConfigureAwait(false);
        if (!installed.IsOk || installed.Value is not { Enabled: true, Error: null } item
            || item.Package.Identity != request.Grant.Package || item.Package.Identity != request.Installation.Package.Identity
            || item.Package.RuntimeKind != PluginRuntimeKind.JavaScript ||
            !((item.Package.Source == PluginPackageSource.Managed && item.Package.RequestedTier == PluginExecutionTier.Privileged) ||
              (item.Package.Source != PluginPackageSource.Managed && item.Package.RequestedTier == PluginExecutionTier.Untrusted)))
            return null;

        var result = await authority.GetCurrentAsync(request.Grant.Package.Id, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Value is not { Enabled: true } grant
            || !request.Grant.Enabled || grant.Package != request.Grant.Package || grant.Generation != request.Grant.Generation
            || grant.Tier != request.Grant.Tier || grant.Tier != item.Package.RequestedTier || !grant.Permissions.SetEquals(request.Grant.Permissions)
            || !grant.AccountKeys.SetEquals(request.Grant.AccountKeys) ||
            !grant.ApprovedHttpOrigins.SetEquals(request.Grant.ApprovedHttpOrigins))
            return null;

        // The activation request takes package metadata from the authenticated store.
        return new PluginRuntimeStart(item, grant, request.Profile, request.Kind, request.Slot, request.Props, request.HostState, request.Fill, request.Callbacks);
    }

    private static Result<IPluginRuntimeInstance> Fail() =>
        Result<IPluginRuntimeInstance>.Fail(new ValidationError("Plugin startup was refused or could not be activated."));
}
