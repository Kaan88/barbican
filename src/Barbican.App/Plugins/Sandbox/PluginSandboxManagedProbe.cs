#if AUTOMATION && WINDOWS
// Synthetic private-desktop evidence for the cross-origin managed sandbox; no package authority is created.
using System.Text.Json;
using Barbican.App.Composition;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Plugins.Sandbox;

internal static class PluginSandboxManagedProbe
{
    private const string Hook = "onManagedProbe";
    private const string Code = """
        module.exports = { hooks: { onManagedProbe: value => {
          let blocked = false;
          try { void parent.document.body; } catch { blocked = true; }
          let attempted = false;
          try {
            if (typeof window.chrome?.webview?.postMessage === 'function') {
              window.chrome.webview.postMessage(JSON.stringify({version:1,root:0,nodes:[
                {id:0,kind:'Object',text:null,flag:false,bytes:null,links:[1,2,3,4],keys:['type','id','method','args'],view:'DataView',offset:0,length:0},
                {id:1,kind:'String',text:'api-request',flag:false,bytes:null,links:null,keys:null,view:'DataView',offset:0,length:0},
                {id:2,kind:'String',text:'direct-probe',flag:false,bytes:null,links:null,keys:null,view:'DataView',offset:0,length:0},
                {id:3,kind:'String',text:'storage.get',flag:false,bytes:null,links:null,keys:null,view:'DataView',offset:0,length:0},
                {id:4,kind:'Array',text:null,flag:false,bytes:null,links:[],keys:null,view:'DataView',offset:0,length:0}
              ]}));
              attempted = true;
            }
          } catch { }
          return location.href + '|' + String(blocked) + '|' + String(attempted) + '|' + value;
        } } };
        """;

    internal static async Task<string> RunAsync(DispatcherQueue queue, Panel mount, IProfilePaths paths,
        HiddenWindowOptions options, string root, CancellationToken ct)
    {
        PluginRuntimeBinding? binding = null;
        PluginSandboxSession? session = null;
        IPluginSandboxWebView? view = null;
        var passed = false;
        var cleaned = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var request = CreateRequest(options.Profile);
            binding = PluginRuntimeBinding.Create(request.Profile, request.Grant, request.Kind);
            var factory = new PluginSandboxWindowsViewFactory(queue, () => mount, paths, options);
            view = await factory.CreateAsync("plugin-" + Guid.NewGuid().ToString("N"), budget.Token).ConfigureAwait(false);
            var dispatcher = new CountingDispatcher();
            session = new PluginSandboxSession(binding, view, dispatcher, new Authority(request.Grant));
            var startup = new PluginSandboxWindowsStartup(root, NullLogger<PluginSandboxWindowsStartup>.Instance);
            var result = await startup.ActivateAsync(session, request, Code, budget.Token).ConfigureAwait(false);
            if (result.IsOk && result.Value is { } instance && instance.Activation.Hooks.Contains(Hook, StringComparer.Ordinal))
            {
                var first = await InvokeAsync(instance, "first", budget.Token).ConfigureAwait(false);
                await Task.Delay(250, budget.Token).ConfigureAwait(false);
                var second = await InvokeAsync(instance, "second", budget.Token).ConfigureAwait(false);
                passed = first && second && dispatcher.Calls == 0 && binding.IsAlive;
            }
        }
#pragma warning disable CA1031 // Probe reports only fixed status, not renderer or profile details.
        catch (Exception) { passed = false; }
#pragma warning restore CA1031
        finally
        {
            try
            {
                if (session is not null) await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                else
                {
                    if (binding is not null) await binding.DisposeAsync().ConfigureAwait(false);
                    if (view is not null) await view.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                }
                cleaned = binding is null || !binding.IsAlive;
            }
#pragma warning disable CA1031 // Cleanup failure is represented by the fixed failure result.
            catch (Exception) { cleaned = false; }
#pragma warning restore CA1031
        }
        return passed && cleaned ? "bootstrap managed ok" : "bootstrap managed failed";
    }

    private static async Task<bool> InvokeAsync(IPluginRuntimeInstance instance, string input, CancellationToken ct)
    {
        var values = new PluginValueBuilder();
        var result = await instance.InvokeHookAsync(Hook, values.Build(values.Array(values.String(input))), TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        return result.IsOk && result.Value is { Kind: PluginValueKind.String } value &&
            string.Equals(value.StringValue, "https://managed-plugin.invalid/sandbox/index.html|true|true|" + input, StringComparison.Ordinal);
    }

    private static PluginRuntimeStart CreateRequest(string profile)
    {
        using var empty = JsonDocument.Parse("{}");
        using var regional = JsonDocument.Parse("""{"locale":"en","dateLocale":"en","hour12":false,"timeZone":"UTC","firstDayOfWeek":1}""");
        using var features = JsonDocument.Parse("""{"externalSenderLabel":false,"externalRecipientWarning":false,"noThirdPartyTraffic":true}""");
        var identity = new PluginPackageIdentity("managed-probe", "1.0.0", new string('a', 64));
        var grant = new PluginGrantSnapshot(identity, 1, true, ["crypto:full"], [], PluginExecutionTier.Privileged);
        var package = new PluginPackageDefinition(identity, PluginRuntimeKind.JavaScript, "index.js", empty.RootElement,
            ["crypto:full"], PluginPackageSource.Managed, PluginExecutionTier.Privileged);
        var state = new PluginHostState("en", regional.RootElement, empty.RootElement, empty.RootElement, features.RootElement);
        var values = new PluginValueBuilder();
        return new(new(package, true, grant), grant, profile, PluginFrameKind.Background, null, values.Build(values.Object()), state);
    }

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginDeliveryAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Result<PluginGrantSnapshot?>.Ok(string.Equals(pluginId, grant.Package.Id, StringComparison.Ordinal) ? grant : null));
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) =>
            Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && binding.Package == grant.Package &&
                binding.GrantGeneration == grant.Generation && binding.Tier == grant.Tier && write(grant));
    }

    private sealed class CountingDispatcher : IPluginApiDispatcher
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(PluginCallOutcome.Failed(PluginErrorCode.Unavailable));
        }
    }
}
#endif
