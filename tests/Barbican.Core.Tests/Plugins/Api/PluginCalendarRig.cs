// Test wiring of the calendar and task add-on methods: the real PluginApiDispatcher over the real calendar writer, invitation service and hook bus on the fake JMAP server (FakeCalendars, SeedM2 for the invitation
// message), an in-memory calendar store, and doubles for the grant, the signed-in session and the things a test flips mid-call (the session going stale, a write that arrived).
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Tests.Calendar.Editing;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginCalendarRig
{
    internal const string Account = "acct";
    internal const string PluginId = "probe";

    private PluginCalendarRig()
    {
    }

    internal FakeJmapServer Server { get; private set; } = null!;

    internal FakeCalendars Cals { get; private set; } = null!;

    internal string Work { get; private set; } = "";

    internal string Home { get; private set; } = "";

    internal MemoryCalendarStore Store { get; private set; } = null!;

    internal HookBus Hooks { get; } = new();

    internal SpyWriter Writer { get; private set; } = null!;

    internal StoreSpy SpyStore { get; private set; } = null!;

    internal AuthorityDouble Authority { get; private set; } = null!;

    internal SessionsDouble Sessions { get; private set; } = null!;

    internal PluginApiDispatcher Dispatcher { get; private set; } = null!;

    internal PluginRuntimeBinding Binding { get; private set; } = null!;

    internal FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

    internal TestHost Host { get; private set; } = null!;

    internal int Sets => Server.Api.SetRequests.Count;

    internal static PluginGrantSnapshot Grant(params string[] permissions) =>
        new(new PluginPackageIdentity(PluginId, "1.0.0", new string('a', 64)), 1, true, permissions.Length == 0 ? ["calendar:read"] : permissions, [Account]);

    internal static async Task<PluginCalendarRig> StartAsync(PluginGrantSnapshot? grant = null, Action<FakeCalendars, string, string>? seed = null, JsonObject? workRights = null, bool invitations = false, bool parsing = true)
    {
        var rig = new PluginCalendarRig();
        var cals = new FakeCalendars();
        rig.Cals = cals;
        rig.Work = cals.AddCalendar("Work", isDefault: true, extra: workRights is null ? null : new JsonObject { ["myRights"] = workRights });
        rig.Home = cals.AddCalendar("Home");
        seed?.Invoke(cals, rig.Work, rig.Home);
        rig.Server = new FakeJmapServer(new FakeJmapOptions { CalendarsCapability = true, AvailabilityCapability = true, Calendars = cals, SeedM2 = invitations });
        var client = await FakeJmapHarness.ConnectAsync(rig.Server, TimeProvider.System, _ => { }, false, TestContext.Current.CancellationToken);
        rig.Store = new MemoryCalendarStore(cals);
        var accounts = new FakeAccounts(client);
        rig.SpyStore = new StoreSpy(rig.Store);
        rig.Writer = new SpyWriter(new CalendarWriter(accounts, rig.Store, rig.Hooks));
        rig.Host = new TestHost(rig.SpyStore, rig.Writer, new InvitationService(accounts, rig.Store, rig.Hooks), rig.Time) { InvitationParsingEnabled = parsing };
        rig.Sessions = new SessionsDouble(new Session(client));
        rig.Authority = new AuthorityDouble(grant ?? Grant());
        rig.Dispatcher = new("auto-cal", PluginCalendarMethods.Create(rig.Host), rig.Authority, rig.Sessions);
        rig.Binding = PluginRuntimeBinding.Create("auto-cal", rig.Authority.Current, PluginFrameKind.Background);
        return rig;
    }

    internal Task<PluginCallOutcome> CallAsync(string method, string args) => CallAsync(Binding, method, args);

    internal async Task<PluginCallOutcome> CallAsync(PluginRuntimeBinding binding, string method, string args)
    {
        using var doc = JsonDocument.Parse(args);
        return await Dispatcher.DispatchAsync(binding, new PluginApiRequest("r", method, PluginValue.FromJson(doc.RootElement)), TestContext.Current.CancellationToken);
    }

    internal JsonObject Event(string id) => Cals.Events().First(e => string.Equals((string?)e["id"], id, StringComparison.Ordinal));

    internal JsonObject? Stored(string uid) => Cals.Events().FirstOrDefault(e => string.Equals((string?)e["uid"], uid, StringComparison.Ordinal));

    internal static List<string> Keys(PluginValue value) => [.. value.PropertyNames.Order(StringComparer.Ordinal)];

    internal static string Text(PluginValue value, string property) => value.Property(property).StringValue;

    internal static double Number(PluginValue value, string property) => double.Parse(value.Property(property).NumberText, System.Globalization.CultureInfo.InvariantCulture);

    internal sealed class TestHost(ICalendarStore store, ICalendarWriter writer, IInvitationService invitations, TimeProvider time) : IPluginCalendarHost
    {
        public ICalendarStore Store { get; } = store;

        public ICalendarWriter Writer { get; } = writer;

        public IInvitationService Invitations { get; } = invitations;

        public ICalendarSubscriptions? Subscriptions { get; set; }

        public PluginUndoTokens Undo { get; } = new(time);

        public TimeProvider Time { get; } = time;

        public TimeZoneInfo LocalZone { get; } = TimeZoneInfo.Utc;

        public string DefaultZoneId => "Europe/Berlin";

        public bool InvitationParsingEnabled { get; set; } = true;
    }

    internal sealed class StoreSpy(ICalendarStore inner) : ICalendarStore
    {
        public Action? OnSnapshot { get; set; }

        public event EventHandler<CalendarChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public Task<Result<CalendarSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => inner.LoadAsync(accountKey, ct);

        public CalendarSnapshot? Snapshot(string accountKey)
        {
            OnSnapshot?.Invoke();
            return inner.Snapshot(accountKey);
        }

        public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => inner.RefreshAsync(accountKey, ct);

        public JsonNode? Patch(string accountKey, string eventId, JsonObject patch) => inner.Patch(accountKey, eventId, patch);

        public void Restore(string accountKey, string eventId, JsonNode? previous) => inner.Restore(accountKey, eventId, previous);
    }

    internal sealed class SpyWriter(ICalendarWriter inner) : ICalendarWriter
    {
        public Action? Before { get; set; }

        public int Calls { get; private set; }

        public Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct) => RunAsync(() => inner.SaveEventAsync(write, ct));

        public Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct) =>
            RunAsync(() => inner.DeleteEventAsync(accountKey, eventId, scope, recurrenceId, notify, ct));

        public Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct) =>
            RunAsync(() => inner.MoveAsync(accountKey, occurrenceId, newStart, duration, ct));

        public Task<Result<string>> CreateAsync(string accountKey, string calendarId, JsonObject data, CancellationToken ct) => RunAsync(() => inner.CreateAsync(accountKey, calendarId, data, ct));

        public Task<Result<Unit>> UpdateAsync(string accountKey, string id, JsonObject patch, CancellationToken ct) => RunAsync(() => inner.UpdateAsync(accountKey, id, patch, ct));

        public Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct) => RunAsync(() => inner.DestroyAsync(accountKey, id, ct));

        private Task<T> RunAsync<T>(Func<Task<T>> call)
        {
            Calls++;
            Before?.Invoke();
            return call();
        }
    }

    internal sealed class AuthorityDouble(PluginGrantSnapshot initial) : IPluginAuthority
    {
        public PluginGrantSnapshot Current { get; set; } = initial;

        public event EventHandler<PluginAuthorityChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<Result<PluginGrantSnapshot?>> GetCurrentAsync(string pluginId, CancellationToken ct) => Task.FromResult(Result<PluginGrantSnapshot?>.Ok(Current));
    }

    internal sealed class SessionsDouble(IAccountSession session) : IPluginSessionSource
    {
        public bool Valid { get; set; } = true;

        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(session, 1));

        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => Result<PluginSessionCapture>.Ok(new(session, null));

        public bool IsCurrent(PluginSessionCapture capture) => Valid && ReferenceEquals(capture.Session, session);

        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write) => Valid && captures.All(IsCurrent) && write();
    }

    internal sealed class Session(IJmapClient client) : IAccountSession
    {
        public AccountInfo Account { get; } = new(PluginCalendarRig.Account, EditingRig.Me, "https://jmap.example.org");

        public IJmapClient Client { get; } = client;

        public IMailService Mail { get; } = new MailService(client, TimeProvider.System);

        public IMailSync Sync => throw new NotSupportedException();
    }

    private sealed class FakeAccounts(IJmapClient client) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [];

        public AccountInfo? Active => null;

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, PluginCalendarRig.Account, StringComparison.Ordinal) ? new Session(client) : null;
    }
}
