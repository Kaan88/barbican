using System.Runtime.CompilerServices;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Native;

internal sealed class NativePackageFactory(NativePlugins plugins, PluginPackageManager packages,
    IPluginRevocationCoordinator revocation) : IPluginRuntimeFactory
{
    public async Task<Result<IPluginRuntimeInstance>> StartAsync(PluginRuntimeStart request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var grant = request.Grant;
        if (!OperatingSystem.IsWindows() || !RuntimeFeature.IsDynamicCodeSupported || request.Kind != PluginFrameKind.Background ||
            request.Slot is not null || request.Installation.Package.Identity != grant.Package ||
            !grant.Enabled || grant.Tier != PluginExecutionTier.NativeFullTrust || grant.AccountKeys.Count != 0 ||
            grant.Permissions.Count != 0) return Failed();

        var binding = PluginRuntimeBinding.Create(request.Profile, grant, PluginFrameKind.Background);
        NativePackageRuntime? runtime = null;
        var transferred = false;
        try
        {
            var inspected = await packages.ReadApprovedNativePackageAsync(binding, ct).ConfigureAwait(false);
            if (!inspected.IsOk || inspected.Value is not { } archive) return Failed();
            var bundled = await plugins.LoadAsync().WaitAsync(ct).ConfigureAwait(false);
            if (bundled.Plugins.Any(plugin => string.Equals(plugin.Id, grant.Package.Id, StringComparison.Ordinal))) return Failed();

            runtime = new NativePackageRuntime(plugins, plugins.CreateArchiveHost(), binding);
            if (!plugins.Reserve(runtime)) return Failed();
            var tracked = revocation.Track(binding, _ => runtime.DisposeAsync());
            if (!tracked.IsOk || tracked.Value is not { } registration) return Failed();
            runtime.Attach(registration);

            var issued = await packages.TryPostAsync(binding,
                _ => binding.TryWriteWhileAlive(() => runtime.QueueStartup(archive)), ct).ConfigureAwait(false);
            if (!issued || !await runtime.WaitForStartupAsync(ct).ConfigureAwait(false)) return Failed();

            if (!await packages.TryPostAsync(binding,
                    _ => binding.TryWriteWhileAlive(runtime.Publish), ct).ConfigureAwait(false))
                return Failed();
            ct.ThrowIfCancellationRequested();
            if (!binding.IsAlive) return Failed();
            transferred = true;
            return Result<IPluginRuntimeInstance>.Ok(runtime);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException) { return Failed(); }
        finally
        {
            if (!transferred)
            {
                if (runtime is null) await binding.DisposeAsync().ConfigureAwait(false);
                else runtime.Abandon();
            }
        }
    }

    private static Result<IPluginRuntimeInstance> Failed() =>
        Result<IPluginRuntimeInstance>.Fail(new ValidationError("Could not start the approved native add-on."));
}
