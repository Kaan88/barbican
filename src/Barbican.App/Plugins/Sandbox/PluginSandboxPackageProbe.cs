#if AUTOMATION && WINDOWS
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Barbican.App.Composition;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Payloads;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Storage;
using Barbican.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Plugins.Sandbox;

// Opt-in hidden automation only. All package data and the witness are fixed in this assembly.
internal static class PluginSandboxPackageProbe
{
    private const string ProfilePrefix = "auto-wv-";
    private const string PackageId = "synthetic-lifecycle-probe";
    private const string Manifest = """{"id":"synthetic-lifecycle-probe","name":"Synthetic lifecycle probe","version":"1.0.0","author":"Example","type":"hook","permissions":["app:lifecycle"]}""";
    private const string Code = """
        let host;
        module.exports = {
          activate(api) { host = api; },
          hooks: { onWindowFocus: async value => {
            if (value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0)
              await host.storage.get('lifecycle-probe');
          } }
        };
        """;

    internal static async Task<string> RunAsync(DispatcherQueue queue, Panel mount, IProfilePaths paths,
        HiddenWindowOptions options, string? bootstrapRoot, CancellationToken ct)
    {
        if (options.Mode != HiddenWindowMode.OffScreen || options.Error is not null ||
            !options.Profile.StartsWith(ProfilePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(options.Profile.AsSpan(ProfilePrefix.Length), "N", out _) ||
            !string.Equals(paths.Profile, options.Profile, StringComparison.Ordinal) ||
            string.IsNullOrEmpty(bootstrapRoot) || !Path.IsPathFullyQualified(bootstrapRoot) ||
            Path.Exists(paths.DataDir) || Path.Exists(paths.ConfigDir))
            return "package probe refused";

        var witness = new Witness();
        var hooks = new HookBus(handlerTimeout: TimeSpan.FromSeconds(8));
        var host = new PluginPackageHost(paths, new InMemorySecretStore(), TimeProvider.System, new EmptyAccounts());
        IPluginRuntimeInstance? instance = null;
        var passed = false;
        var cleaned = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var views = new PluginSandboxWindowsViewFactory(queue, () => mount, paths, options);
            var startup = new PluginSandboxWindowsStartup(bootstrapRoot!, NullLogger<PluginSandboxWindowsStartup>.Instance);
            var factoryResult = await host.CreateSandboxFactoryAsync(views, startup, hooks, new NoSessions(), [witness], budget.Token).ConfigureAwait(false);
            if (!factoryResult.IsOk || factoryResult.Value is not { } factory)
                throw new InvalidOperationException("Synthetic factory unavailable.");

            var request = await InstallAndEnableAsync(host, factory, options.Profile, budget.Token).ConfigureAwait(false);
            var started = await factory.StartAsync(request, budget.Token).ConfigureAwait(false);
            instance = started.Value;
            if (!started.IsOk || instance is not { } active || !active.Binding.IsAlive ||
                !active.Activation.Hooks.Contains(HookNames.OnWindowFocus, StringComparer.Ordinal) ||
                !hooks.HasHandlers(HookNames.OnWindowFocus))
                throw new InvalidOperationException("Synthetic activation failed.");
            await hooks.NotifyAsync(HookNames.OnWindowFocus, new EmptyPayload(), budget.Token).ConfigureAwait(false);
            await witness.Seen.Task.WaitAsync(TimeSpan.FromSeconds(8), budget.Token).ConfigureAwait(false);
            if (witness.Count != 1) throw new InvalidOperationException("Lifecycle witness did not match.");

            if (!(await host.SetEnabledAsync(request.Grant.Package, false, budget.Token).ConfigureAwait(false)).IsOk ||
                active.Binding.IsAlive || hooks.HasHandlers(HookNames.OnWindowFocus))
                throw new InvalidOperationException("Synthetic revocation failed.");
            await hooks.NotifyAsync(HookNames.OnWindowFocus, new EmptyPayload(), budget.Token).ConfigureAwait(false);
            if (!(await host.SetEnabledAsync(request.Grant.Package, true, budget.Token).ConfigureAwait(false)).IsOk)
                throw new InvalidOperationException("Synthetic re-enable failed.");
            await RequireRefusalAsync(factory, request, budget.Token).ConfigureAwait(false);
            if (!(await host.SetEnabledAsync(request.Grant.Package, false, budget.Token).ConfigureAwait(false)).IsOk)
                throw new InvalidOperationException("Synthetic final disable failed.");
            passed = witness.Count == 1;
        }
#pragma warning disable CA1031 // Status must never disclose paths, package content, or renderer messages.
        catch (Exception) { passed = false; }
#pragma warning restore CA1031
        finally { cleaned = await CloseAsync(host, instance, hooks).ConfigureAwait(false); }
        return passed && cleaned ? "package probe ok" : "package probe failed";
    }

    private static async Task<bool> CloseAsync(PluginPackageHost host, IPluginRuntimeInstance? instance, HookBus hooks)
    {
        try
        {
            try
            {
                if (instance is not null) await instance.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false);
            }
            finally { await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).ConfigureAwait(false); }
            return (instance is null || !instance.Binding.IsAlive) && !hooks.HasHandlers(HookNames.OnWindowFocus);
        }
#pragma warning disable CA1031 // Cleanup failure is reported without details.
        catch (Exception) { return false; }
#pragma warning restore CA1031
    }

    private static async Task<PluginRuntimeStart> InstallAndEnableAsync(PluginPackageHost host, IPluginRuntimeFactory factory,
        string profile, CancellationToken ct)
    {
        using var archive = CreateArchive();
        var review = await host.InspectAsync(archive, ct).ConfigureAwait(false);
        if (!review.IsOk || review.Value is not { } preview ||
            !string.Equals(preview.Candidate.Identity.Id, PackageId, StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic inspection failed.");
        var consent = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, ["app:lifecycle"], []);
        var installed = await host.InstallAsync(consent, ct).ConfigureAwait(false);
        if (!installed.IsOk || installed.Value is not { Enabled: false, Grant: { Enabled: false } } disabled ||
            disabled.Package.Identity != preview.Candidate.Identity)
            throw new InvalidOperationException("Synthetic install was not disabled.");
        await RequireRefusalAsync(factory, Request(disabled, profile), ct).ConfigureAwait(false);
        if (!(await host.SetEnabledAsync(disabled.Package.Identity, true, ct).ConfigureAwait(false)).IsOk)
            throw new InvalidOperationException("Synthetic enable failed.");
        var list = await host.ListAsync(ct).ConfigureAwait(false);
        var enabled = list.IsOk && list.Value is { } items ? items.SingleOrDefault(item => item.Package.Identity == disabled.Package.Identity) : null;
        if (enabled is not { Enabled: true, Grant: { Enabled: true } } ||
            enabled.Grant!.Generation == disabled.Grant!.Generation)
            throw new InvalidOperationException("Fresh grant was not issued.");
        return Request(enabled, profile);
    }

    private static async Task RequireRefusalAsync(IPluginRuntimeFactory factory, PluginRuntimeStart request, CancellationToken ct)
    {
        var result = await factory.StartAsync(request, ct).ConfigureAwait(false);
        if (!result.IsOk) return;
        if (result.Value is { } unexpected) await unexpected.DisposeAsync().ConfigureAwait(false);
        throw new InvalidOperationException("Inactive synthetic package started.");
    }

    private static PluginRuntimeStart Request(PluginInstallation installation, string profile)
    {
        using var json = JsonDocument.Parse("{}");
        using var regional = JsonDocument.Parse("""{"locale":"en","dateLocale":"en","hour12":false,"timeZone":"UTC","firstDayOfWeek":1}""");
        using var features = JsonDocument.Parse("""{"externalSenderLabel":false,"externalRecipientWarning":false,"noThirdPartyTraffic":true}""");
        var state = new PluginHostState("en", regional.RootElement, json.RootElement, json.RootElement, features.RootElement);
        var value = new PluginValueBuilder();
        return new PluginRuntimeStart(installation, installation.Grant!, profile, PluginFrameKind.Background, null,
            value.Build(value.Object()), state);
    }

    private static MemoryStream CreateArchive()
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", Manifest);
            Write(zip, "index.js", Code);
        }
        bytes.Position = 0;
        return bytes;
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
    }

    private sealed class EmptyAccounts : IAccountOverview
    {
        public IReadOnlyList<AccountStatus> Statuses => [];
        public event EventHandler? StatusesChanged { add { } remove { } }
    }

    private sealed class NoSessions : IPluginSessionSource
    {
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Fail(new ValidationError("No synthetic account."));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Fail(new ValidationError("No synthetic account."));
        public bool IsCurrent(PluginSessionCapture capture) => false;
    }

    private sealed class Witness : IPluginApiMethod
    {
        private int _count;
        public TaskCompletionSource Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count => Volatile.Read(ref _count);
        public PluginApiDescriptor Descriptor { get; } = new("storage.get", [], TimeSpan.FromSeconds(5));
        public PluginErrorCode? ValidateArguments(PluginValue args) => args.Kind == PluginValueKind.Array && args.Count == 1 &&
            args.Item(0).Kind == PluginValueKind.String && string.Equals(args.Item(0).StringValue, "lifecycle-probe", StringComparison.Ordinal)
                ? null : PluginErrorCode.InvalidArguments;
        public Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue args, CancellationToken ct)
        {
            Interlocked.Increment(ref _count);
            Seen.TrySetResult();
            return Task.FromResult(PluginCallOutcome.Succeeded());
        }
    }
}
#endif
