#if AUTOMATION && WINDOWS
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

// Hidden automation only; no code or account data is accepted from the caller.
internal static class PluginSandboxBootstrapProbe
{
    private const string ProfilePrefix = "auto-wv-";
    private const string ProbeHook = "onBootstrapProbe";
    private const string ProbeCode = "module.exports = { hooks: { onBootstrapProbe: value => 'received:' + value } };";
    private const string NetworkHook = "onNetworkIsolationProbe";
    private const string NetworkProbeCode = """
        module.exports = { hooks: {
          onBootstrapProbe: value => 'received:' + value,
          onNetworkIsolationProbe: async () => {
            if (typeof ReportingObserver !== 'function' || typeof RTCPeerConnection !== 'function') return false;
            let observer;
            try {
              let matched = false;
              const inspect = reports => {
                if (reports.some(report => report.type === 'connection-allowlist' &&
                    report.body?.connection === 'webrtc' && report.body?.disposition === 'enforce')) matched = true;
              };
              observer = new ReportingObserver(inspect, { types: ['connection-allowlist'] });
              observer.observe();
              try { new RTCPeerConnection({ iceServers: [] }).close(); } catch { }
              inspect(observer.takeRecords());
              if (!matched) {
                await new Promise(resolve => setTimeout(resolve, 2000));
                inspect(observer.takeRecords());
              }
              return matched;
            } catch { return false; }
            finally { if (observer) observer.disconnect(); }
          }
        } };
        """;

    public static bool TryStart(HiddenWindowOptions options, out Window? window)
    {
        window = null;
        var root = Environment.GetEnvironmentVariable("BARBICAN_BOOTSTRAP_ROOT");
        if (root is null) return false;

        var paths = ProfilePaths.FromEnvironment();
        if (!Allowed(options, paths, root) || Path.Exists(paths.DataDir) || Path.Exists(paths.ConfigDir))
        {
            Console.WriteLine("bootstrap refused");
            Environment.Exit(2);
            return true;
        }

        var probeWindow = new Window { Title = "Barbican bootstrap probe" };
        var mount = new Grid { IsHitTestVisible = false };
        probeWindow.Content = mount;
        mount.Loaded += OnLoaded;
        HiddenWindow.Show(probeWindow, 320, 180);
        window = probeWindow;
        return true;

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            mount.Loaded -= OnLoaded;
            _ = RunAndExitAsync(probeWindow.DispatcherQueue, mount, paths, options, root);
        }
    }

    private static async Task RunAndExitAsync(DispatcherQueue queue, Panel mount, IProfilePaths paths, HiddenWindowOptions options, string root)
    {
        var slot = IsRequested("BARBICAN_BOOTSTRAP_CHECK_SLOT");
        var network = IsRequested("BARBICAN_BOOTSTRAP_CHECK_NETWORK");
        var package = IsRequested("BARBICAN_BOOTSTRAP_CHECK_PACKAGE");
        var status = (slot, network, package) switch
        {
            (true, false, false) => await PluginSandboxSlotProbe.RunAsync(queue, mount, paths, options, root, CancellationToken.None).ConfigureAwait(false),
            (false, false, true) => await PluginSandboxPackageProbe.RunAsync(queue, mount, paths, options, root, CancellationToken.None).ConfigureAwait(false),
            (false, _, false) => await RunAsync(queue, mount, paths, options, root, network, CancellationToken.None).ConfigureAwait(false),
            _ => "bootstrap refused",
        };
        Console.WriteLine(status);
        Environment.Exit(status is "bootstrap ok" or "bootstrap network ok" or "bootstrap slot ok" or "package probe ok" ? 0 : 1);
    }

    private static bool IsRequested(string variable) =>
        string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal);

    private static bool Allowed(HiddenWindowOptions options, IProfilePaths paths, string? bootstrapRoot) =>
        options.Mode == HiddenWindowMode.OffScreen && options.Error is null &&
        options.Profile.StartsWith(ProfilePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(options.Profile.AsSpan(ProfilePrefix.Length), "N", out _) &&
        string.Equals(paths.Profile, options.Profile, StringComparison.Ordinal) &&
        !string.IsNullOrEmpty(bootstrapRoot) && Path.IsPathFullyQualified(bootstrapRoot);

    private static async Task<string> RunAsync(DispatcherQueue queue, Panel mount, IProfilePaths paths,
        HiddenWindowOptions options, string? bootstrapRoot, bool networkCheck, CancellationToken ct)
    {
        if (!Allowed(options, paths, bootstrapRoot))
            return "bootstrap refused";

        PluginSandboxSession? session = null;
        PluginRuntimeBinding? binding = null;
        IPluginSandboxWebView? view = null;
        var passed = false;
        var cleaned = false;
        var networkPassed = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var request = CreateRequest(options.Profile);
            binding = PluginRuntimeBinding.Create(request.Profile, request.Grant, request.Kind);
            var views = new PluginSandboxWindowsViewFactory(queue, () => mount, paths, options);
            view = await views.CreateAsync("plugin-" + Guid.NewGuid().ToString("N"), budget.Token).ConfigureAwait(false);
            session = new PluginSandboxSession(binding, view, new RefusingDispatcher(), new ProbeAuthority(request.Grant));
            var startup = new PluginSandboxWindowsStartup(bootstrapRoot!, NullLogger<PluginSandboxWindowsStartup>.Instance, networkCheck);
            var result = await startup.ActivateAsync(session, request, networkCheck ? NetworkProbeCode : ProbeCode, budget.Token).ConfigureAwait(false);
            passed = result.IsOk && result.Value is { } instance &&
                await VerifyHookAsync(instance, networkCheck, budget.Token).ConfigureAwait(false);
            if (passed && networkCheck && result.Value is { } active)
                networkPassed = await VerifyNetworkAsync(active, budget.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Probe output must not disclose paths or renderer messages.
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
#pragma warning disable CA1031 // A failed teardown is reported without exception details.
            catch (Exception) { cleaned = false; }
#pragma warning restore CA1031
        }
        if (!passed || !cleaned) return "bootstrap failed";
        if (!networkCheck) return "bootstrap ok";
        return networkPassed ? "bootstrap network ok" : "bootstrap network inconclusive";
    }

    private static PluginRuntimeStart CreateRequest(string profile)
    {
        using var json = JsonDocument.Parse("{}");
        using var regional = JsonDocument.Parse("""{"locale":"en","dateLocale":"en","hour12":false,"timeZone":"UTC","firstDayOfWeek":1}""");
        using var features = JsonDocument.Parse("""{"externalSenderLabel":false,"externalRecipientWarning":false,"noThirdPartyTraffic":true}""");
        var identity = new PluginPackageIdentity("bootstrap-probe", "1.0.0", new string('a', 64));
        var grant = new PluginGrantSnapshot(identity, 1, true, [], []);
        var package = new PluginPackageDefinition(identity, PluginRuntimeKind.JavaScript, "index.js",
            json.RootElement, [], PluginPackageSource.LocalImport);
        var installation = new PluginInstallation(package, true, grant);
        var state = new PluginHostState("en", regional.RootElement, json.RootElement, json.RootElement, features.RootElement);
        var values = new PluginValueBuilder();
        return new PluginRuntimeStart(installation, grant, profile, PluginFrameKind.Background,
            null, values.Build(values.Object()), state);
    }

    private static async Task<bool> VerifyHookAsync(IPluginRuntimeInstance instance, bool networkCheck, CancellationToken ct)
    {
        if (!instance.Binding.IsAlive || instance.Activation.Hooks.Length != (networkCheck ? 2 : 1) ||
            !instance.Activation.Hooks.Contains(ProbeHook, StringComparer.Ordinal) ||
            (networkCheck && !instance.Activation.Hooks.Contains(NetworkHook, StringComparer.Ordinal)) ||
            !instance.Activation.Slots.IsEmpty || !instance.Activation.Shortcuts.IsEmpty) return false;
        var arguments = new PluginValueBuilder();
        var result = await instance.InvokeHookAsync(ProbeHook, arguments.Build(arguments.Array(arguments.String("fixture"))),
            TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        return result.IsOk && result.Value is { Kind: PluginValueKind.String } value &&
            string.Equals(value.StringValue, "received:fixture", StringComparison.Ordinal);
    }

    private static async Task<bool> VerifyNetworkAsync(IPluginRuntimeInstance instance, CancellationToken ct)
    {
        try
        {
            var arguments = new PluginValueBuilder();
            var result = await instance.InvokeHookAsync(NetworkHook, arguments.Build(arguments.Array()),
                TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            return result.IsOk && result.Value is { Kind: PluginValueKind.Boolean } value && value.BooleanValue;
        }
#pragma warning disable CA1031 // A missing or timed-out observation has one fixed inconclusive result.
        catch (Exception) { return false; }
#pragma warning restore CA1031
    }

    private sealed class ProbeAuthority(PluginGrantSnapshot grant) : IPluginDeliveryAuthority
    {
        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }
        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Result<PluginGrantSnapshot?>.Ok(string.Equals(pluginId, grant.Package.Id, StringComparison.Ordinal) ? grant : null));
        public Task<bool> TryPostAsync(PluginRuntimeBinding binding, Func<PluginGrantSnapshot, bool> write, CancellationToken ct) =>
            Task.FromResult(!ct.IsCancellationRequested && binding.IsAlive && binding.Package == grant.Package &&
                binding.GrantGeneration == grant.Generation && binding.Tier == grant.Tier && write(grant));
    }

    private sealed class RefusingDispatcher : IPluginApiDispatcher
    {
        public Task<PluginCallOutcome> DispatchAsync(PluginRuntimeBinding binding, PluginApiRequest request, CancellationToken ct) =>
            Task.FromResult(PluginCallOutcome.Failed(PluginErrorCode.Unavailable));
    }

}
#endif
