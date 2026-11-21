// Test wiring of the filters.*, sieve.*, vacation.*, identities.update, accountData.*, plugin.getConfig and notifications.show methods: the real PluginApiDispatcher over the real FilterService, VacationService and AccountIdentities
// on the fake JMAP server (sieve, vacation and metadata capabilities on), the real hook bus, and switchable doubles for the grant and the signed-in session. Each service is wrapped so a test can flip the grant or the session
// when a read or a write starts (the account going stale before or during a write).
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Plugins.Api.AccountSettings;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.MailRules;
using Barbican.Core.Plugins.Api.MailTags;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Settings.Account;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginRulesRig
{
    private PluginRulesRig()
    {
    }

    public Fake F { get; private set; } = null!;

    public PluginApiRig.Authority Auth { get; private set; } = null!;

    public PluginApiRig.Sessions Active { get; private set; } = null!;

    public PluginApiDispatcher Dispatcher { get; private set; } = null!;

    public PluginRuntimeBinding Binding { get; private set; } = null!;

    public HookBus Hooks { get; private set; } = null!;

    public FakePrefs Prefs { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

    public FilterService Filters { get; private set; } = null!;

    public VacationService Vacation { get; private set; } = null!;

    public AccountIdentities Identities { get; private set; } = null!;

    public FakeApi Api => F.Server.Api;

    /// <summary>Runs when a service call starts a read (or a write): flip the grant or the session here.</summary>
    public Action? OnRead { get; set; }

    public Action? OnWrite { get; set; }

    /// <summary>Changes what a vacation read returns (the filters script that could not be read).</summary>
    public Func<VacationSettings, VacationSettings>? ShapeSnapshot { get; set; }

    /// <summary>The administrator settings by add-on id that <c>plugin.getConfig</c> can see.</summary>
    public Dictionary<string, Dictionary<string, JsonNode?>> Config { get; } = new(StringComparer.Ordinal);

    /// <summary>The toasts the presenter was asked to show, with the add-on id of the call.</summary>
    public List<(string Plugin, string Message)> Toasts { get; } = [];

    /// <summary>The Inbox metadata the server holds.</summary>
    public JsonObject InboxMetadata => F.Server.Store.MailboxMetadata(F.Server.Store.MailboxId("inbox")!);

    public static async Task<PluginRulesRig> StartAsync(string[] permissions, FakeJmapOptions? options = null)
    {
        var rig = new PluginRulesRig();
        var clock = Fake.VirtualClock();
        var o = (options ?? new FakeJmapOptions { SieveCapability = true, VacationCapability = true, MetadataCapability = true }) with { Clock = clock };
        rig.F = await Fake.StartAsync(o, clock);
        var session = new PluginApiRig.Session(rig.F, () => null);
        rig.Active = new PluginApiRig.Sessions(session);
        rig.Auth = new PluginApiRig.Authority(PluginApiRig.Grant(permissions));
        rig.Hooks = new HookBus(clock);
        var accounts = new RigAccounts(session);
        rig.Filters = new FilterService(accounts, rig.Hooks);
        rig.Vacation = new VacationService(accounts, rig.Hooks, clock);
        rig.Identities = new AccountIdentities(accounts, rig.Hooks);
        var tags = new PluginTagDefinitions(rig.Prefs, rig.Prefs);
        var limiter = new PluginMailRateLimiter(rig.Time);
        var config = new RigConfig(rig);
        var rules = new PluginRulesMethodSet(new HookedFilters(rig.Filters, rig), tags, limiter, rig.Time);
        var settings = new PluginAccountSettingsMethodSet(new HookedVacation(rig.Vacation, rig), new HookedIdentities(rig.Identities, rig), config, limiter,
            async (message, context, ct) =>
            {
                // the host's presenter rechecks the authority right before it shows anything
                if (!await context.CheckCurrentAsync(ct)) return PluginErrorCode.StaleContext;
                rig.Toasts.Add((context.Binding.Package.Id, message));
                context.MarkMutationIssued();
                return null;
            });
        rig.Dispatcher = new("auto-api", [.. rules.Methods, .. settings.Methods], rig.Auth, rig.Active);
        rig.Binding = PluginRuntimeBinding.Create("auto-api", rig.Auth.Current, PluginFrameKind.Background);
        return rig;
    }

    public Task<PluginCallOutcome> CallAsync(string method, string args) => CallAsync(Binding, method, args);

    public async Task<PluginCallOutcome> CallAsync(PluginRuntimeBinding binding, string method, string args)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(args);
        return await Dispatcher.DispatchAsync(binding, new PluginApiRequest("r", method, PluginValue.FromJson(doc.RootElement)), TestContext.Current.CancellationToken);
    }

    /// <summary>accountData.set for the login's own account: the first argument is JavaScript's undefined, which JSON cannot spell.</summary>
    public async Task<PluginCallOutcome> SetDataAsync(string valueJson, PluginRuntimeBinding? binding = null)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(valueJson);
        var b = new PluginValueBuilder();
        var args = b.Build(b.Array(b.Undefined(), b.Import(PluginValue.FromJson(doc.RootElement))));
        return await Dispatcher.DispatchAsync(binding ?? Binding, new PluginApiRequest("r", "accountData.set", args), TestContext.Current.CancellationToken);
    }

    public PluginRuntimeBinding BindOther(string packageId, string[] permissions) => BindOtherCore(packageId, permissions);

    private PluginRuntimeBinding BindOtherCore(string packageId, string[] permissions)
    {
        var grant = new Barbican.Core.Plugins.Permissions.PluginGrantSnapshot(new Barbican.Core.Plugins.Packages.PluginPackageIdentity(packageId, "1.0.0", new string('b', 64)), 1, true, permissions, ["a1"]);
        Auth.Others[packageId] = grant;
        return PluginRuntimeBinding.Create("auto-api", grant, PluginFrameKind.Background);
    }

    /// <summary>The id of the folder with this role (inbox, archive, trash, ...).</summary>
    public string Folder(string role) => F.Server.Store.MailboxId(role)!;

    private sealed class RigAccounts(IAccountSession session) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [session.Account];

        public AccountInfo? Active => session.Account;

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged { add { } remove { } }

        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, session.Account.Key, StringComparison.Ordinal) ? session : null;
    }

    private sealed class RigConfig(PluginRulesRig rig) : IPluginPublicConfig
    {
        public Task<IReadOnlyDictionary<string, JsonNode?>> ReadAsync(string pluginId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, JsonNode?>>(rig.Config.TryGetValue(pluginId, out var own) ? own.ToDictionary(e => e.Key, e => e.Value?.DeepClone(), StringComparer.Ordinal) : []);
    }

    private sealed class HookedFilters(IFilterService inner, PluginRulesRig rig) : IFilterService
    {
        public event EventHandler<FilterChangedEventArgs>? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }

        public Task<Result<FilterState>> LoadAsync(string accountKey, CancellationToken ct) { rig.OnRead?.Invoke(); return inner.LoadAsync(accountKey, ct); }

        public Task<Result<FilterState>> SaveAsync(string accountKey, IReadOnlyList<JsonNode> rules, string? expectedState, CancellationToken ct) { rig.OnWrite?.Invoke(); return inner.SaveAsync(accountKey, rules, expectedState, ct); }

        public Task<Result<FilterScan>> ScanAsync(string accountKey, JsonNode rule, long generation, CancellationToken ct) => inner.ScanAsync(accountKey, rule, generation, ct);

        public Task<Result<FilterApplied>> ApplyAsync(string accountKey, FilterScan scan, CancellationToken ct) => inner.ApplyAsync(accountKey, scan, ct);
    }

    private sealed class HookedVacation(IVacationService inner, PluginRulesRig rig) : IVacationService
    {
        public event EventHandler<VacationChangedEventArgs>? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }

        public Task<Result<VacationSettings>> LoadAsync(string accountKey, CancellationToken ct) { rig.OnRead?.Invoke(); return inner.LoadAsync(accountKey, ct); }

        public async Task<Result<VacationSettings>> LoadSnapshotAsync(string accountKey, CancellationToken ct)
        {
            rig.OnRead?.Invoke();
            var loaded = await inner.LoadSnapshotAsync(accountKey, ct).ConfigureAwait(false);
            return loaded.IsOk && rig.ShapeSnapshot is { } shape ? Result<VacationSettings>.Ok(shape(loaded.Value!)) : loaded;
        }

        public Task<Result<VacationSettings>> SaveAsync(string accountKey, VacationSettings settings, CancellationToken ct) { rig.OnWrite?.Invoke(); return inner.SaveAsync(accountKey, settings, ct); }
    }

    private sealed class HookedIdentities(IAccountIdentities inner, PluginRulesRig rig) : IAccountIdentities
    {
        public event EventHandler<AccountIdentityChangedEventArgs>? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }

        public Task<Result<IReadOnlyList<AccountIdentity>>> ListAsync(string accountKey, CancellationToken ct) { rig.OnRead?.Invoke(); return inner.ListAsync(accountKey, ct); }

        public Task<Result<AccountIdentity>> CreateAsync(string accountKey, AccountIdentityEdit edit, CancellationToken ct) => inner.CreateAsync(accountKey, edit, ct);

        public Task<Result<AccountIdentity>> UpdateAsync(string accountKey, string id, AccountIdentityEdit edit, CancellationToken ct) { rig.OnWrite?.Invoke(); return inner.UpdateAsync(accountKey, id, edit, ct); }

        public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => inner.DeleteAsync(accountKey, id, ct);

        public Task<Result<Unit>> SetOrderAsync(string accountKey, IReadOnlyList<string> orderedIds, CancellationToken ct) => inner.SetOrderAsync(accountKey, orderedIds, ct);
    }
}
