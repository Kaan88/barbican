using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Platform;
using Barbican.Platform.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginPackageHostShutdownTests
{
    [Fact]
    public async Task RemovalCanBeRetriedAfterDataErasureFailsAndTheHostReopens()
    {
        var root = Directory.CreateTempSubdirectory("barbican-host-test-");
        var paths = ProfilePaths.At("fixture-erase-retry", root.FullName, root.FullName);
        var secrets = new InMemorySecretStore();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var opened = await new PluginProfileAuthorityLeaseFactory().AcquireAsync(paths, ct);
            Assert.True(opened.IsOk);
            await using (var lease = opened.Value!)
            {
                var stored = await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, ct);
                Assert.True(stored.IsOk);
                using var store = stored.Value!;
                Assert.True((await store.CommitAsync(paths.Profile, "fixture-plugin", "synthetic-login", 0, new byte[] { 1 }, ct)).IsOk);
            }
            var dataRoot = Path.Combine(root.FullName, "plugins", "packages", "user-v1");
            var scoped = Assert.Single(Directory.GetDirectories(dataRoot));
            var blocker = Path.Combine(scoped, "unexpected.txt");
            await File.WriteAllTextAsync(blocker, "synthetic", ct);

            var host = new PluginPackageHost(paths, secrets, TimeProvider.System, new Accounts());
            PluginPackageIdentity package;
            await using (host)
            {
                package = (await InstallEnabledAsync(host, ct)).Package.Identity;
                Assert.False((await host.UninstallAsync(package, ct)).IsOk);
                Assert.False((await host.FindAsync(package.Id, ct)).Value!.Enabled);
                Assert.Single(Directory.GetFiles(scoped, "*.data"));
            }

            File.Delete(blocker);
            await using var reopened = new PluginPackageHost(paths, secrets, TimeProvider.System, new Accounts());
            Assert.True((await reopened.UninstallAsync(package, ct)).IsOk);
            Assert.Empty((await reopened.ListAsync(ct)).Value!);
            Assert.False(Directory.Exists(scoped));
        }
        finally { root.Delete(recursive: true); }
    }

    [Fact]
    public async Task Disable_and_window_drain_handle_a_restore_blocked_before_runtime_tracking()
    {
        var root = Directory.CreateTempSubdirectory("barbican-host-test-");
        var paths = ProfilePaths.At("fixture-blocked-start", root.FullName, root.FullName);
        var host = new PluginPackageHost(paths, new InMemorySecretStore(), TimeProvider.System, new Accounts());
        await using var runtimes = new PluginBackgroundRuntimes(host, paths, new Prefs(), NullLogger<PluginBackgroundRuntimes>.Instance);
        var views = new BlockingViews();
        var startup = new Startup();
        startup.Release.SetResult();
        var ct = TestContext.Current.CancellationToken;
        Task? restore = null;
        try
        {
            var active = await InstallEnabledAsync(host, ct, "active-fixture");
            var item = await InstallEnabledAsync(host, ct, "stalled-fixture");
            restore = runtimes.InitializeAsync(token => host.CreateSandboxFactoryAsync(views, startup, new HookBus(), new Sessions(), [], token), ct);
            await views.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8), ct);
            var disabled = await runtimes.SetEnabledAsync(item.Package.Identity, false, ct).WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.True(disabled.IsOk);
            Assert.False((await host.FindAsync(item.Package.Identity.Id, ct)).Value!.Enabled);
            Assert.False(runtimes.IsRunning(item));
            Assert.True(runtimes.IsRunning(active));

            var drained = runtimes.StopAsync();
            await host.BeginRuntimeShutdownAsync();
            Assert.False(drained.IsCompleted);
            Assert.False(runtimes.IsRunning(active));
            Assert.False((await new PluginProfileAuthorityLeaseFactory().AcquireAsync(paths, ct)).IsOk);
            views.Release.SetResult();
            await restore.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.True(await drained);
            Assert.Equal(1, startup.Activations);
        }
        finally
        {
            views.Release.TrySetResult();
            if (restore is not null) await restore.WaitAsync(TimeSpan.FromSeconds(8), CancellationToken.None);
            startup.Release.TrySetResult();
            await host.DisposeAsync();
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Restore_and_enable_share_one_runtime_and_a_reenable_uses_a_fresh_grant()
    {
        var root = Directory.CreateTempSubdirectory("barbican-host-test-");
        var paths = ProfilePaths.At("fixture-restore", root.FullName, root.FullName);
        var host = new PluginPackageHost(paths, new InMemorySecretStore(), TimeProvider.System, new Accounts());
        await using var runtimes = new PluginBackgroundRuntimes(host, paths, new Prefs(), NullLogger<PluginBackgroundRuntimes>.Instance);
        var startup = new Startup(validateProjectedState: true);
        startup.Release.SetResult();
        var ct = TestContext.Current.CancellationToken;
        try
        {
            var item = await InstallEnabledAsync(host, ct);
            await runtimes.InitializeAsync(token => host.CreateSandboxFactoryAsync(new Views(), startup, new HookBus(), new Sessions(), [], token), ct);
            var projected = Assert.IsType<PluginRuntimeStart>(startup.LastRequest);
            Assert.Equal("America/New_York", projected.HostState.Regional.GetProperty("timeZone").GetString());
            Assert.Equal(1, projected.HostState.Regional.GetProperty("firstDayOfWeek").GetInt32());
            Assert.True(runtimes.IsRunning(item));
            Assert.True((await runtimes.SetEnabledAsync(item.Package.Identity, true, ct)).IsOk);
            Assert.Equal(1, startup.Activations);

            Assert.True((await runtimes.SetEnabledAsync(item.Package.Identity, false, ct)).IsOk);
            Assert.False(runtimes.IsRunning(item));
            Assert.True((await runtimes.SetEnabledAsync(item.Package.Identity, true, ct)).IsOk);
            var current = Assert.Single((await host.ListAsync(ct)).Value!);
            Assert.False(runtimes.IsRunning(item));
            Assert.True(runtimes.IsRunning(current));
            Assert.Equal(2, startup.Activations);
            Assert.True((await runtimes.UninstallAsync(item.Package.Identity, ct)).IsOk);
            Assert.False(runtimes.IsRunning(current));
            Assert.Empty((await host.ListAsync(ct)).Value!);
        }
        finally
        {
            await host.DisposeAsync();
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Start_can_retry_factory_creation_after_transient_store_failure()
    {
        var root = Directory.CreateTempSubdirectory("barbican-host-test-");
        var paths = ProfilePaths.At("fixture-retry", root.FullName, root.FullName);
        var host = new PluginPackageHost(paths, new InMemorySecretStore(), TimeProvider.System, new Accounts());
        await using var runtimes = new PluginBackgroundRuntimes(host, paths, new Prefs(), NullLogger<PluginBackgroundRuntimes>.Instance);
        var startup = new Startup();
        startup.Release.SetResult();
        var ct = TestContext.Current.CancellationToken;
        var attempts = 0;
        try
        {
            var installed = await InstallEnabledAsync(host, ct);
            await runtimes.InitializeAsync(token => ++attempts == 1
                ? Task.FromResult(Result<IPluginRuntimeFactory>.Fail(new ValidationError("Store temporarily unavailable.")))
                : host.CreateSandboxFactoryAsync(new Views(), startup, new HookBus(), new Sessions(), [], token), ct);
            Assert.False(runtimes.IsRunning(installed));
            Assert.True((await runtimes.SetEnabledAsync(installed.Package.Identity, true, ct)).IsOk);
            Assert.True(runtimes.IsRunning(installed));
            Assert.Equal(2, attempts);
            Assert.Equal(1, startup.Activations);
        }
        finally
        {
            await host.DisposeAsync();
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Shutdown_shares_failure_invalidates_runtime_and_releases_the_lease()
    {
        var root = Directory.CreateTempSubdirectory("barbican-host-test-");
        var paths = ProfilePaths.At("fixture-shutdown", root.FullName, root.FullName);
        var host = new PluginPackageHost(paths, new InMemorySecretStore(), TimeProvider.System, new Accounts());
        var startup = new Startup();
        var ct = TestContext.Current.CancellationToken;
        var shutdownFailed = false;
        try
        {
            var current = await InstallEnabledAsync(host, ct);
            var factoryResult = await host.CreateSandboxFactoryAsync(new Views(), startup, new HookBus(), new Sessions(), [], ct);
            Assert.True(factoryResult.IsOk);
            var factory = factoryResult.Value!;
            var request = Start(current, paths.Profile);
            var started = await factory.StartAsync(request, ct);
            Assert.True(started.IsOk);
            var binding = started.Value!.Binding;

            var first = host.ShutdownAsync();
            var second = host.ShutdownAsync();
            await startup.Closing.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.False(binding.IsAlive);
            var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(12), ct);
            Assert.All(results, result => Assert.False(result.IsOk));
            shutdownFailed = true;
            Assert.False((await factory.StartAsync(request, ct)).IsOk);
            Assert.False((await host.CreateSandboxFactoryAsync(new Views(), startup, new HookBus(), new Sessions(), [], ct)).IsOk);

            var leaseResult = await new PluginProfileAuthorityLeaseFactory().AcquireAsync(paths, ct);
            Assert.True(leaseResult.IsOk);
            await leaseResult.Value!.DisposeAsync();
        }
        finally
        {
            startup.Release.TrySetResult();
            if (startup.WasClosing) await startup.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            if (shutdownFailed) await Assert.ThrowsAsync<InvalidOperationException>(() => host.DisposeAsync().AsTask());
            else await host.DisposeAsync();
            root.Delete(recursive: true);
        }
    }

    private static async Task<PluginInstallation> InstallEnabledAsync(PluginPackageHost host, CancellationToken ct, string id = "fixture-plugin")
    {
        using var archive = Archive(id);
        var previewResult = await host.InspectAsync(archive, ct);
        Assert.True(previewResult.IsOk);
        var preview = previewResult.Value!;
        var installed = await host.InstallAsync(new(preview.ReviewId, preview.Candidate.Identity, [], []), ct);
        Assert.True(installed.IsOk);
        Assert.True((await host.SetEnabledAsync(preview.Candidate.Identity, true, ct)).IsOk);
        var enabled = await host.FindAsync(id, ct);
        Assert.True(enabled.IsOk);
        return enabled.Value!;
    }

    private static PluginRuntimeStart Start(PluginInstallation installation, string profile)
    {
        using var empty = JsonDocument.Parse("{}");
        var builder = new PluginValueBuilder();
        return new(installation, installation.Grant!, profile, PluginFrameKind.Background, null, builder.Build(builder.Object()),
            new PluginHostState("en", empty.RootElement, empty.RootElement, empty.RootElement, empty.RootElement));
    }

    private static MemoryStream Archive(string id)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "manifest.json", $$"""{"id":"{{id}}","name":"Fixture","version":"1.0.0","author":"Example","type":"hook","permissions":[]}""");
            Add(zip, "index.js", "export default {}");
        }
        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var output = entry.Open();
        output.Write(Encoding.UTF8.GetBytes(text));
    }

    private sealed class Accounts : IAccountOverview
    {
        public IReadOnlyList<AccountStatus> Statuses => [];
        public event EventHandler? StatusesChanged { add { } remove { } }
    }
    private sealed class Sessions : IPluginSessionSource
    {
        public Result<PluginSessionCapture> CaptureActive() => throw new NotSupportedException();
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => throw new NotSupportedException();
        public bool IsCurrent(PluginSessionCapture capture) => false;
    }
    private sealed class Prefs : IPrefsStore
    {
        public PrefsSnapshot Current { get; } = PrefsSnapshot.FromJson(new JsonObject
        {
            ["timeZone"] = "America/New_York",
            ["firstDayOfWeek"] = 2L,
        });
        public event EventHandler? Changed { add { } remove { } }
    }
    private sealed class Views : IPluginSandboxWebViewFactory
    {
        public Task<IPluginSandboxWebView> CreateAsync(string profile, CancellationToken ct) => Task.FromResult<IPluginSandboxWebView>(new View());
    }
    private sealed class View : IPluginSandboxWebView
    {
        public object RootIdentity { get; } = new();
        public Uri TrustedRoot { get; } = new("https://plugin.invalid/host/index.html");
        public Task NavigateAsync(Uri uri, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> PostAsync(Func<Action<string>, CancellationToken, ValueTask<bool>> deliver, CancellationToken ct) => deliver(_ => { }, ct).AsTask();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class BlockingViews : IPluginSandboxWebViewFactory
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IPluginSandboxWebView> CreateAsync(string profile, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) == 1) return new View();
            Entered.SetResult();
#pragma warning disable VSTHRD003 // Simulates a native creation call that cannot be canceled while in flight.
            await Release.Task;
#pragma warning restore VSTHRD003
            ct.ThrowIfCancellationRequested();
            return new View();
        }
    }
    private sealed class Startup(bool validateProjectedState = false) : IPluginSandboxStartup
    {
        public TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasClosing => Closing.Task.IsCompleted;
        public int Activations { get; private set; }
        public PluginRuntimeStart? LastRequest { get; private set; }
        public Task<Result<IPluginRuntimeInstance>> ActivateAsync(PluginSandboxSession session, PluginRuntimeStart trustedRequest, string verifiedCode, CancellationToken ct)
        {
            LastRequest = trustedRequest;
            if (validateProjectedState)
                _ = PluginSandboxInitMessage.Create(trustedRequest, verifiedCode);
            Activations++;
            return Task.FromResult(Result<IPluginRuntimeInstance>.Ok(new Runtime(session.Binding, this)));
        }
    }
    private sealed class Runtime(PluginRuntimeBinding binding, Startup startup) : IPluginRuntimeInstance
    {
        public PluginRuntimeBinding Binding { get; } = binding;
        public PluginActivation Activation { get; } = new([], [], []);
        public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => throw new NotSupportedException();
        public async ValueTask DisposeAsync()
        {
            startup.Closing.TrySetResult();
#pragma warning disable VSTHRD003 // The test completes this signal asynchronously after observing both shutdown callers.
            await startup.Release.Task;
#pragma warning restore VSTHRD003
            startup.Closed.TrySetResult();
        }
    }
}
