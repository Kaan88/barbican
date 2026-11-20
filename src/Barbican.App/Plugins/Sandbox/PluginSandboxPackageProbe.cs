#if AUTOMATION && WINDOWS
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.App.Composition;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
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

// Opt-in hidden automation only. Package data and both synthetic login sessions are fixed in this assembly.
internal static class PluginSandboxPackageProbe
{
    private const string ProfilePrefix = "auto-wv-";
    private const string PackageId = "synthetic-lifecycle-probe";
    private const string OtherPackageId = "synthetic-oauth-other";
    private const string ProbeHook = "onStorageProbe";
    private const string OAuthProbeHook = "onOAuthProbe";
    private const string HttpProbeHook = "onHttpFetchProbe";
    private const string FirstLogin = "synthetic-login-a";
    private const string SecondLogin = "synthetic-login-b";
    private const string Manifest = """{"id":"synthetic-lifecycle-probe","name":"Synthetic lifecycle probe","version":"1.0.0","author":"Example","type":"hook","permissions":["app:lifecycle","http:fetch"],"httpOrigins":["https://probe.example.org"]}""";
    private const string Code = """
        let host;
        let responses = [];
        module.exports = {
          activate(api) { host = api; },
          hooks: {
            onOAuthCallback: value => { responses.push(value); },
            onOAuthProbe: async action => action === 'issue'
              ? { state: await host.oauth.createState(), redirect: await host.oauth.getRedirectUri() }
              : responses,
            onHttpFetchProbe: async path => {
              try {
                const response = await host.http.fetch('https://probe.example.org/' + path);
                const max = 25 * 1024 * 1024;
                if (path !== 'max') return 'redirect unexpectedly succeeded';
                return response.ok && response.status === 200 && response.statusText === 'OK' &&
                  response.headers['x-probe'] === 'synthetic' &&
                  response.bodyBytes instanceof Uint8Array && response.bodyBytes.length === max &&
                  response.bodyText.length === max && response.bodyText[0] === 'x' && response.bodyText[max - 1] === 'x' &&
                  !Object.hasOwn(response, 'decodeFetchBody') ? 'body verified' : 'unexpected response shape';
              } catch (error) {
                if (error?.message === 'Plugin permission denied.') return 'permission denied';
                if (error?.message === 'Unknown plugin operation.') return 'method unavailable';
                if (error?.message === 'Invalid plugin operation arguments.') return 'arguments refused';
                return 'unexpected fetch error';
              }
            },
            onWindowFocus: async value => {
              if (value && typeof value === 'object' && !Array.isArray(value) && Object.keys(value).length === 0)
                await host.storage.set('lifecycle-probe', 'focus');
            },
            onStorageProbe: async action => {
              if (action === 'write') await host.storage.set('lifecycle-probe', 'second');
              if (action === 'remove') await host.storage.remove('lifecycle-probe');
              const value = await host.storage.get('lifecycle-probe');
              const keys = await host.storage.keys();
              if (value === null && keys.length === 0) return 'empty';
              if (keys.length === 1 && keys[0] === 'lifecycle-probe') return value;
              return 'unexpected';
            }
          }
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

        var accounts = new ProbeAccounts();
        var hooks = new HookBus(handlerTimeout: TimeSpan.FromSeconds(8));
        var host = new PluginPackageHost(paths, new InMemorySecretStore(), TimeProvider.System, accounts) { UseSyntheticHttpProbe = true };
        IPluginRuntimeInstance? instance = null;
        var passed = false;
        var cleaned = false;
        var phase = "factory";
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(52));
        try
        {
            var views = new PluginSandboxWindowsViewFactory(queue, () => mount, paths, options);
            var startup = new PluginSandboxWindowsStartup(bootstrapRoot!, NullLogger<PluginSandboxWindowsStartup>.Instance);
            var factoryResult = await host.CreateAddOnFactoryAsync(views, startup, hooks, accounts, [], includeUserStorage: true,
                nativePlugins: null, oauthBrowser: null, networkPrefs: new ProbeNetworkPrefs(), budget.Token).ConfigureAwait(false);
            if (!factoryResult.IsOk || factoryResult.Value is not { } factory)
                throw new InvalidOperationException("Synthetic factory unavailable.");

            var request = await InstallAndEnableAsync(host, factory, options.Profile, PackageId, budget.Token).ConfigureAwait(false);
            var started = await factory.StartAsync(request, budget.Token).ConfigureAwait(false);
            instance = started.Value;
            if (!started.IsOk || instance is not { } active || !active.Binding.IsAlive ||
                !active.Activation.Hooks.Contains(HookNames.OnWindowFocus, StringComparer.Ordinal) ||
                !hooks.HasHandlers(HookNames.OnWindowFocus))
                throw new InvalidOperationException("Synthetic activation failed.");
            await hooks.NotifyAsync(HookNames.OnWindowFocus, new EmptyPayload(), budget.Token).ConfigureAwait(false);
            await VerifyStorageAsync(active, accounts, budget.Token).ConfigureAwait(false);
            phase = "HTTP fetch";
            await VerifyHttpFetchAsync(active, budget.Token).ConfigureAwait(false);
            phase = "OAuth";
            var lateCallback = await VerifyOAuthAsync(host, factory, active, options.Profile, budget.Token).ConfigureAwait(false);

            if (!(await host.SetEnabledAsync(request.Grant.Package, false, budget.Token).ConfigureAwait(false)).IsOk ||
                active.Binding.IsAlive || hooks.HasHandlers(HookNames.OnWindowFocus))
                throw new InvalidOperationException("Synthetic revocation failed.");
            await ExpectOAuthCallbackAsync(lateCallback, HttpStatusCode.BadRequest, budget.Token).ConfigureAwait(false);
            await hooks.NotifyAsync(HookNames.OnWindowFocus, new EmptyPayload(), budget.Token).ConfigureAwait(false);
            if (!(await host.SetEnabledAsync(request.Grant.Package, true, budget.Token).ConfigureAwait(false)).IsOk)
                throw new InvalidOperationException("Synthetic re-enable failed.");
            await RequireRefusalAsync(factory, request, budget.Token).ConfigureAwait(false);
            instance = await VerifyRestartAsync(host, factory, request, accounts, budget.Token).ConfigureAwait(false);
            passed = true;
        }
#pragma warning disable CA1031 // Status must never disclose paths, package content, or renderer messages.
        catch (Exception) { Console.WriteLine("package probe failed at " + phase); }
#pragma warning restore CA1031
        finally { cleaned = await CloseAsync(host, instance, hooks).ConfigureAwait(false); }
        return passed && cleaned ? "package probe ok" : "package probe failed";
    }

    private static async Task VerifyHttpFetchAsync(IPluginRuntimeInstance runtime, CancellationToken ct)
    {
        foreach (var (path, expected) in new[] { ("redirect", "permission denied"), ("max", "body verified") })
        {
            var builder = new PluginValueBuilder();
            var result = await runtime.InvokeHookAsync(HttpProbeHook, builder.Build(builder.Array(builder.String(path))),
                TimeSpan.FromSeconds(35), ct).ConfigureAwait(false);
            var observed = result.IsOk && result.Value is { Kind: PluginValueKind.String } value ? value.StringValue : "no hook result";
            if (!string.Equals(observed, expected, StringComparison.Ordinal))
            {
                Console.WriteLine("synthetic HTTP fetch probe " + path + ": " + observed);
                throw new InvalidOperationException("Synthetic HTTP fetch did not preserve the authorized response or redirect refusal.");
            }
        }
    }

    private static async Task<Uri> VerifyOAuthAsync(PluginPackageHost host, IPluginRuntimeFactory factory,
        IPluginRuntimeInstance owner, string profile, CancellationToken ct)
    {
        var request = await InstallAndEnableAsync(host, factory, profile, OtherPackageId, ct).ConfigureAwait(false);
        var result = await factory.StartAsync(request, ct).ConfigureAwait(false);
        if (!result.IsOk || result.Value is not { } other) throw new InvalidOperationException("Second add-on failed to start.");
        await using var scope = other.ConfigureAwait(false);
        var callback = await IssueOAuthCallbackAsync(owner, ct).ConfigureAwait(false);
        await ExpectOAuthCallbackAsync(callback, HttpStatusCode.OK, ct).ConfigureAwait(false);
        await ExpectOAuthCallbackAsync(callback, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
        var observed = await OAuthProbeAsync(owner, "read", ct).ConfigureAwait(false);
        var isolated = await OAuthProbeAsync(other, "read", ct).ConfigureAwait(false);
        if (observed.Kind != PluginValueKind.Array || observed.Count != 1 || isolated.Kind != PluginValueKind.Array || isolated.Count != 0 ||
            !string.Equals(observed.Item(0).Property("code").StringValue, "synthetic-grant", StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic OAuth callback delivery was not isolated.");
        if (!(await host.UninstallAsync(request.Grant.Package, ct).ConfigureAwait(false)).IsOk)
            throw new InvalidOperationException("Second add-on removal failed.");
        return await IssueOAuthCallbackAsync(owner, ct).ConfigureAwait(false);
    }

    private static async Task<Uri> IssueOAuthCallbackAsync(IPluginRuntimeInstance runtime, CancellationToken ct)
    {
        var issued = await OAuthProbeAsync(runtime, "issue", ct).ConfigureAwait(false);
        var state = issued.Property("state").StringValue;
        if (state.Length != 48 || !state.All(char.IsAsciiHexDigitLower) ||
            !Uri.TryCreate(issued.Property("redirect").StringValue, UriKind.Absolute, out var redirect) ||
            !string.Equals(redirect.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) ||
            !string.Equals(redirect.Host, "127.0.0.1", StringComparison.Ordinal) ||
            !string.Equals(redirect.AbsolutePath, "/plugins/oauth/callback", StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic OAuth redirect is unavailable.");
        return new UriBuilder(redirect) { Query = "state=" + state + "&code=synthetic-grant" }.Uri;
    }

    private static async Task<PluginValue> OAuthProbeAsync(IPluginRuntimeInstance runtime, string action, CancellationToken ct)
    {
        var builder = new PluginValueBuilder();
        var result = await runtime.InvokeHookAsync(OAuthProbeHook, builder.Build(builder.Array(builder.String(action))),
            TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        return result.IsOk && result.Value is { } value ? value : throw new InvalidOperationException("Synthetic OAuth hook failed.");
    }

    private static async Task ExpectOAuthCallbackAsync(Uri uri, HttpStatusCode expected, CancellationToken ct)
    {
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false });
        using var response = await client.GetAsync(uri, ct).ConfigureAwait(false);
        if (response.StatusCode != expected) throw new InvalidOperationException("Synthetic OAuth browser reply was unexpected.");
    }

    private static async Task VerifyStorageAsync(IPluginRuntimeInstance active, ProbeAccounts accounts, CancellationToken ct)
    {
        await RequireStorageAsync(active, "read", "focus", ct).ConfigureAwait(false);
        accounts.SwitchToSecond(true);
        await RequireStorageAsync(active, "read", "empty", ct).ConfigureAwait(false);
        await RequireStorageAsync(active, "write", "second", ct).ConfigureAwait(false);
        accounts.SwitchToSecond(false);
        await RequireStorageAsync(active, "read", "focus", ct).ConfigureAwait(false);
        accounts.SwitchToSecond(true);
        await RequireStorageAsync(active, "remove", "empty", ct).ConfigureAwait(false);
        accounts.SwitchToSecond(false);
    }

    private static async Task<IPluginRuntimeInstance> VerifyRestartAsync(PluginPackageHost host, IPluginRuntimeFactory factory,
        PluginRuntimeStart request, ProbeAccounts accounts, CancellationToken ct)
    {
        var current = await host.FindAsync(PackageId, ct).ConfigureAwait(false);
        if (!current.IsOk || current.Value is not { Enabled: true, Grant: { Enabled: true } } installation)
            throw new InvalidOperationException("Synthetic grant unavailable.");
        var restarted = await factory.StartAsync(Request(installation, request.Profile), ct).ConfigureAwait(false);
        if (!restarted.IsOk || restarted.Value is not { } runtime)
            throw new InvalidOperationException("Synthetic restart failed.");
        try
        {
            await RequireStorageAsync(runtime, "read", "focus", ct).ConfigureAwait(false);
            await VerifySignOutAsync(host, runtime, accounts, ct).ConfigureAwait(false);
            if (!(await host.UninstallAsync(installation.Package.Identity, ct).ConfigureAwait(false)).IsOk || runtime.Binding.IsAlive)
                throw new InvalidOperationException("Synthetic removal failed.");
            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task VerifySignOutAsync(PluginPackageHost host, IPluginRuntimeInstance runtime, ProbeAccounts accounts,
        CancellationToken ct)
    {
        accounts.SwitchToSecond(true);
        await RequireStorageAsync(runtime, "write", "second", ct).ConfigureAwait(false);

        accounts.SetSignOutFence(true, allAccounts: false);
        var erased = await host.PrepareSignOutAsync([FirstLogin], allAccounts: false, ct).ConfigureAwait(false);
        if (!erased.IsOk) throw new InvalidOperationException("Synthetic single-account erasure failed.");
        accounts.SetSignOutFence(false, allAccounts: false);
        await RequireStorageAsync(runtime, "read", "second", ct).ConfigureAwait(false);
        accounts.SwitchToSecond(false);
        await RequireStorageAsync(runtime, "read", "empty", ct).ConfigureAwait(false);
        await RequireStorageAsync(runtime, "write", "second", ct).ConfigureAwait(false);

        accounts.SetSignOutFence(true, allAccounts: true);
        erased = await host.PrepareSignOutAsync([FirstLogin, SecondLogin], allAccounts: true, ct).ConfigureAwait(false);
        if (!erased.IsOk) throw new InvalidOperationException("Synthetic full sign-out erasure failed.");
        accounts.SetSignOutFence(false, allAccounts: true);
        await RequireStorageAsync(runtime, "read", "empty", ct).ConfigureAwait(false);
        accounts.SwitchToSecond(true);
        await RequireStorageAsync(runtime, "read", "empty", ct).ConfigureAwait(false);
    }

    private static async Task RequireStorageAsync(IPluginRuntimeInstance runtime, string action, string expected, CancellationToken ct)
    {
        var builder = new PluginValueBuilder();
        var arguments = builder.Build(builder.Array(builder.String(action)));
        var result = await runtime.InvokeHookAsync(ProbeHook, arguments, TimeSpan.FromSeconds(8), ct).ConfigureAwait(false);
        if (!result.IsOk || result.Value is not { Kind: PluginValueKind.String } value || !string.Equals(value.StringValue, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic storage did not match.");
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
        string profile, string id, CancellationToken ct)
    {
        using var archive = CreateArchive(id);
        var review = await host.InspectAsync(archive, ct).ConfigureAwait(false);
        if (!review.IsOk || review.Value is not { } preview ||
            !string.Equals(preview.Candidate.Identity.Id, id, StringComparison.Ordinal))
            throw new InvalidOperationException("Synthetic inspection failed.");
        var consent = new PluginInstallDecision(preview.ReviewId, preview.Candidate.Identity, ["app:lifecycle", "http:fetch"], [FirstLogin, SecondLogin],
            approvedHttpOrigins: preview.Candidate.RequestedHttpOrigins);
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

    private static MemoryStream CreateArchive(string id)
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", Manifest.Replace(PackageId, id, StringComparison.Ordinal));
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

    private sealed class ProbeAccounts : IAccountOverview, IPluginSessionSource
    {
        private readonly Lock _gate = new();
        private ProbeSession _first = new(FirstLogin);
        private ProbeSession _second = new(SecondLogin);
        private long _generation = 1;
        private bool _useSecond;
        private bool _blockFirst;
        private bool _blockSecond;

        public IReadOnlyList<AccountStatus> Statuses
        {
            get
            {
                lock (_gate) return [new(_first.Account, false, true, true, null, null), new(_second.Account, false, false, true, null, null)];
            }
        }
        public event EventHandler? StatusesChanged { add { } remove { } }
        public void SwitchToSecond(bool useSecond)
        {
            lock (_gate) { _useSecond = useSecond; _generation++; }
        }
        public void SetSignOutFence(bool pending, bool allAccounts)
        {
            lock (_gate)
            {
                _blockFirst = pending;
                _blockSecond = pending && allAccounts;
                _generation++;
                if (pending) return;
                _first = new ProbeSession(FirstLogin);
                if (allAccounts) _second = new ProbeSession(SecondLogin);
            }
        }
        public Result<PluginSessionCapture> CaptureActive()
        {
            lock (_gate)
            {
                return (_useSecond ? _blockSecond : _blockFirst)
                    ? Result<PluginSessionCapture>.Fail(new ValidationError("The synthetic login is signing out."))
                    : Result<PluginSessionCapture>.Ok(new(_useSecond ? _second : _first, _generation));
            }
        }
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) =>
            Result<PluginSessionCapture>.Fail(new ValidationError("No explicit login is used by this probe."));
        public bool IsCurrent(PluginSessionCapture capture)
        {
            lock (_gate) return Current(capture);
        }
        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write)
        {
            lock (_gate) return captures.All(Current) && write();
        }
        private bool Current(PluginSessionCapture capture) =>
            ((ReferenceEquals(capture.Session, _first) && !_blockFirst) || (ReferenceEquals(capture.Session, _second) && !_blockSecond)) &&
            (capture.ActiveSelectionGeneration is null ||
                (capture.ActiveSelectionGeneration == _generation && ReferenceEquals(capture.Session, _useSecond ? _second : _first)));
    }

    private sealed class ProbeSession(string key) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "example", "https://jmap.example.org");
        public bool AllowsAddOnStorage => true;
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }

    private sealed class ProbeNetworkPrefs : IPrefsStore
    {
        public PrefsSnapshot Current { get; } = PrefsSnapshot.FromJson(new JsonObject { [PluginNetworkPolicy.Preference] = false });
        public event EventHandler? Changed { add { } remove { } }
    }
}
#endif
