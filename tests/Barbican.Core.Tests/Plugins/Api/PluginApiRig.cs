// Test wiring for the account, app, tag and template methods: the real PluginApiDispatcher over a FakeJmap login, with a switchable grant and session
// (the pattern of PluginMailMethodsTests, shared here so each family of methods has its own small test file).
using System.Text.Json;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.FakeJmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Tests.Jmap;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginApiRig
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public Fake F { get; private set; } = null!;

    public Authority Auth { get; private set; } = null!;

    public Sessions Active { get; private set; } = null!;

    public PluginApiDispatcher Dispatcher { get; private set; } = null!;

    public PluginRuntimeBinding Binding { get; private set; } = null!;

    public AccountInfo Account => Active.Account;

    /// <summary>Runs when an add-on method calls the mail service: flip the grant or session here to go stale mid-call.</summary>
    public Action? OnMailCall { get; set; }

    public static PluginGrantSnapshot Grant(string[] permissions, bool enabled = true, long generation = 1) =>
        new(new PluginPackageIdentity("probe", "1.0.0", new string('a', 64)), generation, enabled, permissions, ["a1"]);

    public static async Task<PluginApiRig> StartAsync(Func<IReadOnlyList<IPluginApiMethod>> methods, string[] permissions, FakeJmapOptions? options = null)
    {
        var rig = new PluginApiRig { F = await Fake.StartAsync(options, Fake.VirtualClock()) };
        rig.Active = new Sessions(new Session(rig.F, () => rig.OnMailCall));
        rig.Auth = new Authority(Grant(permissions));
        rig.Dispatcher = new("auto-api", methods(), rig.Auth, rig.Active);
        rig.Binding = PluginRuntimeBinding.Create("auto-api", rig.Auth.Current, PluginFrameKind.Background);
        return rig;
    }

    public Task<PluginCallOutcome> CallAsync(string method, string args) => CallAsync(Binding, method, args);

    public async Task<PluginCallOutcome> CallAsync(PluginRuntimeBinding binding, string method, string args)
    {
        using var doc = JsonDocument.Parse(args);
        return await Dispatcher.DispatchAsync(binding, new PluginApiRequest("r", method, PluginValue.FromJson(doc.RootElement)), Ct);
    }

    /// <summary>A second add-on (its own package id) granted <paramref name="permissions"/> on the same login.</summary>
    public PluginRuntimeBinding BindOther(string packageId, string[] permissions)
    {
        var grant = new PluginGrantSnapshot(new PluginPackageIdentity(packageId, "1.0.0", new string('b', 64)), 1, true, permissions, ["a1"]);
        Auth.Others[packageId] = grant;
        return PluginRuntimeBinding.Create("auto-api", grant, PluginFrameKind.Background);
    }

    public static List<string> Keys(PluginValue value) => [.. value.PropertyNames.Order(StringComparer.Ordinal)];

    internal sealed class Authority(PluginGrantSnapshot initial) : IPluginAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = initial;

        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed { add { } remove { } }

        /// <summary>Grants of other add-ons by package id (the main add-on is <see cref="Current"/>).</summary>
        public Dictionary<string, PluginGrantSnapshot> Others { get; } = new(StringComparer.Ordinal);

        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Others.TryGetValue(pluginId, out var other) ? other : Current));
    }

    internal sealed class Sessions(IAccountSession session) : IPluginSessionSource
    {
        public bool Valid { get; set; } = true;

        public AccountInfo Account => session.Account;

        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(session, 1));

        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Ok(new(session, null));

        public bool IsCurrent(PluginSessionCapture capture) => Valid && ReferenceEquals(capture.Session, session);

        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) => Valid && captures.All(IsCurrent) && write();
    }

    internal sealed class Session(Fake f, Func<Action?> hook) : IAccountSession
    {
        public AccountInfo Account { get; } = new("a1", "user@example.org", "https://jmap.example.org");

        public IJmapClient Client { get; } = f.Client;

        public IMailService Mail { get; } = new HookedMail(new MailService(f.Client, f.Time), hook);

        public IMailSync Sync => throw new NotSupportedException();
    }
}
