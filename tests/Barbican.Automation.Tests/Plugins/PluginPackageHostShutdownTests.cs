using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Barbican.App.Plugins;
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Platform;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginPackageHostShutdownTests
{
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

    private static async Task<PluginInstallation> InstallEnabledAsync(PluginPackageHost host, CancellationToken ct)
    {
        using var archive = Archive();
        var previewResult = await host.InspectAsync(archive, ct);
        Assert.True(previewResult.IsOk);
        var preview = previewResult.Value!;
        var installed = await host.InstallAsync(new(preview.ReviewId, preview.Candidate.Identity, [], []), ct);
        Assert.True(installed.IsOk);
        Assert.True((await host.SetEnabledAsync(preview.Candidate.Identity, true, ct)).IsOk);
        return Assert.Single((await host.ListAsync(ct)).Value!);
    }

    private static PluginRuntimeStart Start(PluginInstallation installation, string profile)
    {
        using var empty = JsonDocument.Parse("{}");
        var builder = new PluginValueBuilder();
        return new(installation, installation.Grant!, profile, PluginFrameKind.Background, null, builder.Build(builder.Object()),
            new PluginHostState("en", empty.RootElement, empty.RootElement, empty.RootElement, empty.RootElement));
    }

    private static MemoryStream Archive()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            Add(zip, "manifest.json", """{"id":"fixture-plugin","name":"Fixture","version":"1.0.0","author":"Example","type":"hook","permissions":[]}""");
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
    private sealed class Startup : IPluginSandboxStartup
    {
        public TaskCompletionSource Closing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasClosing => Closing.Task.IsCompleted;
        public Task<Result<IPluginRuntimeInstance>> ActivateAsync(PluginSandboxSession session, PluginRuntimeStart trustedRequest, string verifiedCode, CancellationToken ct) =>
            Task.FromResult(Result<IPluginRuntimeInstance>.Ok(new Runtime(session.Binding, this)));
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
