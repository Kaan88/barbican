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
    private const string ProbeCode = "module.exports = { hooks: { onBootstrapProbe: () => null } };";

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
        var status = await RunAsync(queue, mount, paths, options, root, CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(status);
        Environment.Exit(string.Equals(status, "bootstrap ok", StringComparison.Ordinal) ? 0 : 1);
    }

    private static bool Allowed(HiddenWindowOptions options, IProfilePaths paths, string? bootstrapRoot) =>
        options.Mode == HiddenWindowMode.OffScreen && options.Error is null &&
        options.Profile.StartsWith(ProfilePrefix, StringComparison.Ordinal) &&
        Guid.TryParseExact(options.Profile.AsSpan(ProfilePrefix.Length), "N", out _) &&
        string.Equals(paths.Profile, options.Profile, StringComparison.Ordinal) &&
        !string.IsNullOrEmpty(bootstrapRoot) && Path.IsPathFullyQualified(bootstrapRoot);

    internal static async Task<string> RunAsync(DispatcherQueue queue, Panel mount, IProfilePaths paths,
        HiddenWindowOptions options, string? bootstrapRoot, CancellationToken ct)
    {
        if (!Allowed(options, paths, bootstrapRoot))
            return "bootstrap refused";

        PluginSandboxSession? session = null;
        PluginRuntimeBinding? binding = null;
        IPluginSandboxWebView? view = null;
        var passed = false;
        var cleaned = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var json = JsonDocument.Parse("{}");
            using var regional = JsonDocument.Parse("""{"locale":"en","dateLocale":"en","hour12":false,"timeZone":"UTC","firstDayOfWeek":1}""");
            using var features = JsonDocument.Parse("""{"externalSenderLabel":false,"externalRecipientWarning":false,"noThirdPartyTraffic":true}""");
            var identity = new PluginPackageIdentity("bootstrap-probe", "1.0.0", new string('a', 64));
            var grant = new PluginGrantSnapshot(identity, 1, true, [], []);
            var package = new PluginPackageDefinition(identity, PluginRuntimeKind.JavaScript, "index.js",
                json.RootElement, [], PluginPackageSource.LocalImport);
            var installation = new PluginInstallation(package, true, grant, null);
            var state = new PluginHostState("en", regional.RootElement, json.RootElement, json.RootElement, features.RootElement);
            var values = new PluginValueBuilder();
            var request = new PluginRuntimeStart(installation, grant, options.Profile, PluginFrameKind.Background,
                null, values.Build(values.Object()), state);
            binding = PluginRuntimeBinding.Create(options.Profile, grant, PluginFrameKind.Background);
            var views = new PluginSandboxWindowsViewFactory(queue, () => mount, paths, options);
            view = await views.CreateAsync("plugin-" + Guid.NewGuid().ToString("N"), budget.Token).ConfigureAwait(false);
            session = new PluginSandboxSession(binding, view, new RefusingDispatcher(), new ProbeAuthority(grant));
            var startup = new PluginSandboxWindowsStartup(bootstrapRoot!,
                (current, activation) => new ProbeInstance(current.Binding, activation),
                NullLogger<PluginSandboxWindowsStartup>.Instance);
            var result = await startup.ActivateAsync(session, request, ProbeCode, budget.Token).ConfigureAwait(false);
            passed = result.IsOk && result.Value is { } instance && binding.IsAlive &&
                instance.Activation.Hooks.Length == 1 &&
                string.Equals(instance.Activation.Hooks[0], ProbeHook, StringComparison.Ordinal) &&
                instance.Activation.Slots.IsEmpty && instance.Activation.Shortcuts.IsEmpty;
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
        return passed && cleaned ? "bootstrap ok" : "bootstrap failed";
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

    private sealed class ProbeInstance(PluginRuntimeBinding binding, PluginActivation activation) : IPluginRuntimeInstance
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginActivation Activation { get; } = activation;
        public Task<Result<PluginValue>> InvokeHookAsync(string name, PluginValue args, TimeSpan budget, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PluginValue>> InvokeCallbackAsync(string id, PluginValue args, TimeSpan budget, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SendAsync(PluginValue message, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
#endif
