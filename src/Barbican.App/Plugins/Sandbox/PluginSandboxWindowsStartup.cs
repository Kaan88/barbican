#if WINDOWS
using System.Collections.Immutable;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Performs the trusted-parent and verified-runtime handshake on Windows.</summary>
internal sealed class PluginSandboxWindowsStartup(
    string bootstrapRoot,
    Func<PluginSandboxSession, PluginActivation, IPluginRuntimeInstance> createInstance,
    ILogger<PluginSandboxWindowsStartup> log) : IPluginSandboxStartup
{
    private static readonly string[] HostScripts = ["plugin-sandbox-host.js", "plugin-sandbox-parent.js", "plugin-value-codec.js"];

    public async Task<Result<IPluginRuntimeInstance>> ActivateAsync(PluginSandboxSession session, PluginRuntimeStart trustedRequest,
        string verifiedCode, CancellationToken ct)
    {
        if (session.View is not PluginSandboxWindowsView view || view.RootIdentity is not CoreWebView2 core)
            return Refused();

        var init = PluginSandboxInitMessage.Create(trustedRequest, verifiedCode);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(20));
        var token = budget.Token;
        var runtimeAssets = await PluginSandboxBootstrapAssets.LoadAsync(bootstrapRoot, token).ConfigureAwait(false);
        var hostScripts = await LoadHostScriptsAsync(token).ConfigureAwait(false);
        var resources = await PluginSandboxWindowsResources.PrepareAsync(session.Binding, runtimeAssets, hostScripts, token).ConfigureAwait(false);
        await view.AttachGuardsAsync(resources, () => new PluginSandboxWindowsEvents(session, resources.Attach(core), log), token).ConfigureAwait(false);
        await view.NavigateAsync(view.TrustedRoot, token).ConfigureAwait(false);
        await session.WaitForReadyAsync(token).ConfigureAwait(false);
        if (!await session.IssueInitAsync(init, token).ConfigureAwait(false)) return Refused();
        var acknowledgement = await session.WaitForInitializationAsync(token).ConfigureAwait(false);
        if (acknowledgement is null || !session.Binding.IsAlive) return Refused();
        token.ThrowIfCancellationRequested();
        var activation = PluginSandboxInitMessage.ParseActivation(acknowledgement);
        return Result<IPluginRuntimeInstance>.Ok(createInstance(session, activation));
    }

    private static async Task<ImmutableDictionary<string, ImmutableArray<byte>>> LoadHostScriptsAsync(CancellationToken ct)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Assets", "plugins");
        var result = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var name in HostScripts)
        {
            var path = Path.Combine(root, name);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is < 1 or > 1024 * 1024) throw new InvalidDataException("Invalid plugin host script size.");
            var bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
            result.Add(name, ImmutableArray.CreateRange(bytes));
        }
        return result.ToImmutable();
    }

    private static Result<IPluginRuntimeInstance> Refused() =>
        Result<IPluginRuntimeInstance>.Fail(new ValidationError("Windows plugin activation was refused."));
}
#endif
