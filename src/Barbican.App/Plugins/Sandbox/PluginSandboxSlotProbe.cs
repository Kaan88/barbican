#if AUTOMATION && WINDOWS
// Automation-only slot evidence using the pinned Bulwark renderer, runtime.tsx:1495-1603.
using System.Text.Json;
using Barbican.App.Composition;
using Barbican.App.Shell;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Barbican.App.Plugins.Sandbox;

internal static class PluginSandboxSlotProbe
{
    private const string Slot = "sidebar-widget";
    private const string Code = "module.exports = { slots: { 'sidebar-widget': { component: p => React.createElement('div', { id: 'synthetic-slot-proof', style: { width: 123, height: 37 } }, p.label) } } };";
    private const string Check = "(() => { const e = document.getElementById('synthetic-slot-proof'); if (location.href !== 'https://plugin.invalid/sandbox/index.html?frame=slot' || self.origin !== 'null' || !e || e.textContent !== 'synthetic slot') return false; const r = e.getBoundingClientRect(); return r.width === 123 && r.height === 37; })()";

    internal static async Task<string> RunAsync(DispatcherQueue queue, Panel mount, IProfilePaths paths,
        HiddenWindowOptions options, string root, CancellationToken ct)
    {
        var ui = new DispatcherQueueDispatcher(queue);
        PluginRuntimeBinding? binding = null;
        PluginSandboxSession? session = null;
        IPluginSandboxWebView? view = null;
        CoreWebView2? core = null;
        CoreWebView2Frame? frame = null;
        var frameCount = 0;
        var passed = false;
        var cleaned = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        void FrameCreated(CoreWebView2 sender, CoreWebView2FrameCreatedEventArgs args)
        {
            frameCount++;
            if (frameCount == 1) frame = args.Frame;
        }
        try
        {
            var request = CreateRequest(options.Profile);
            binding = PluginRuntimeBinding.Create(request.Profile, request.Grant, PluginFrameKind.Slot, Slot);
            var factory = new PluginSandboxWindowsViewFactory(queue, () => mount, paths, options);
            view = await factory.CreateAsync("plugin-" + Guid.NewGuid().ToString("N"), budget.Token).ConfigureAwait(false);
            core = (CoreWebView2)view.RootIdentity;
            await ui.InvokeAsync(() => { core.FrameCreated += FrameCreated; return Task.CompletedTask; }).ConfigureAwait(false);
            session = new PluginSandboxSession(binding, view, new RefusingDispatcher(), new Authority(request.Grant));
            var startup = new PluginSandboxWindowsStartup(root, NullLogger<PluginSandboxWindowsStartup>.Instance);
            var result = await startup.ActivateAsync(session, request, Code, budget.Token).ConfigureAwait(false);
            if (result.IsOk && result.Value is not null && binding.IsAlive)
            {
                for (var i = 0; i < 30 && !passed; i++)
                {
                    await ui.InvokeAsync(async () =>
                    {
                        if (binding.IsAlive && frameCount == 1 && frame is not null)
                            passed = string.Equals(await frame.ExecuteScriptAsync(Check), "true", StringComparison.Ordinal);
                    }).WaitAsync(budget.Token).ConfigureAwait(false);
                    if (!passed) await Task.Delay(100, budget.Token).ConfigureAwait(false);
                }
            }
        }
#pragma warning disable CA1031 // A probe failure reports no renderer or profile data.
        catch (Exception) { passed = false; }
#pragma warning restore CA1031
        finally
        {
            cleaned = await CloseAsync(() => ui.InvokeAsync(() =>
            {
                if (core is not null) core.FrameCreated -= FrameCreated;
                return Task.CompletedTask;
            }), session, binding, view).ConfigureAwait(false);
        }
        return passed && cleaned ? "bootstrap slot ok" : "bootstrap slot failed";
    }

    private static async Task<bool> CloseAsync(Func<Task> unsubscribe, PluginSandboxSession? session,
        PluginRuntimeBinding? binding, IPluginSandboxWebView? view)
    {
        try
        {
            try { await unsubscribe().ConfigureAwait(false); }
            finally
            {
                if (session is not null) await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                else
                {
                    if (binding is not null) await binding.DisposeAsync().ConfigureAwait(false);
                    if (view is not null) await view.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
                }
            }
            return binding is null || !binding.IsAlive;
        }
#pragma warning disable CA1031 // A cleanup failure is reported without exception details.
        catch (Exception) { return false; }
#pragma warning restore CA1031
    }

    private static PluginRuntimeStart CreateRequest(string profile)
    {
        using var empty = JsonDocument.Parse("{}");
        using var regional = JsonDocument.Parse("""{"locale":"en","dateLocale":"en","hour12":false,"timeZone":"UTC","firstDayOfWeek":1}""");
        using var theme = JsonDocument.Parse("""{"dark":false,"fontFamily":"sans-serif","vars":{}}""");
        using var features = JsonDocument.Parse("""{"externalSenderLabel":false,"externalRecipientWarning":false,"noThirdPartyTraffic":true}""");
        var identity = new PluginPackageIdentity("slot-probe", "1.0.0", new string('a', 64));
        var grant = new PluginGrantSnapshot(identity, 1, true, [], []);
        var package = new PluginPackageDefinition(identity, PluginRuntimeKind.JavaScript, "index.js", empty.RootElement, [], PluginPackageSource.LocalImport);
        var state = new PluginHostState("en", regional.RootElement, theme.RootElement, empty.RootElement, features.RootElement);
        var values = new PluginValueBuilder();
        var props = values.Build(values.Object(("label", values.String("synthetic slot"))));
        return new(new(package, true, grant), grant, profile, PluginFrameKind.Slot, Slot, props, state);
    }

    private sealed class Authority(PluginGrantSnapshot grant) : IPluginDeliveryAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(string.Equals(pluginId, grant.Package.Id, StringComparison.Ordinal) ? grant : null));
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) => Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && binding.Package == grant.Package && binding.GrantGeneration == grant.Generation && binding.Tier == grant.Tier && write(grant));
    }

    private sealed class RefusingDispatcher : IPluginApiDispatcher
    {
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct) => Task.FromResult(PluginCallOutcome.Failed(PluginErrorCode.Unavailable));
    }
}
#endif
