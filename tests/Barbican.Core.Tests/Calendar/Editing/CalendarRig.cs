// Test wiring of the calendar writers over the fake JMAP server (M4 stream cal-edit): a connected client, a real hook bus, an in-memory store that records the optimistic patches and their restores, and the user's login as the organiser.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Hooks;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Calendar.Editing;

internal sealed class CalendarRig
{
    public const string Account = "acct";
    public const string Me = "user@example.org";

    private CalendarRig(FakeJmapServer server, FakeCalendars cals, string work, JmapClient client)
    {
        Server = server;
        Cals = cals;
        Work = work;
        Client = client;
        Store = new MemoryCalendarStore(cals);
        var accounts = new FakeAccounts(client);
        Accounts = accounts;
        Writer = new CalendarWriter(accounts, Store, Hooks);
        Admin = new CalendarAdmin(accounts, Store);
    }

    public FakeJmapServer Server { get; }

    public IAccountManager Accounts { get; }

    public FakeCalendars Cals { get; }

    public string Work { get; }

    public JmapClient Client { get; }

    public HookBus Hooks { get; } = new();

    public MemoryCalendarStore Store { get; }

    public CalendarWriter Writer { get; }

    public CalendarAdmin Admin { get; }

    public IReadOnlyList<(string Type, JsonObject Args)> Sets => Server.Api.SetRequests;

    public static async Task<CalendarRig> CreateAsync(Action<FakeCalendars, string>? seed = null)
    {
        var cals = new FakeCalendars();
        var work = cals.AddCalendar("Work", isDefault: true);
        seed?.Invoke(cals, work);
        var server = new FakeJmapServer(new FakeJmapOptions { CalendarsCapability = true, AvailabilityCapability = true, Calendars = cals });
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, TestContext.Current.CancellationToken);
        return new CalendarRig(server, cals, work, client);
    }

    /// <summary>A weekday series of five days at 09:00 (UTC) with the user as organiser and Ann as a guest.</summary>
    public static JsonObject Series(string id = "s") => new()
    {
        ["id"] = id,
        ["uid"] = "u-" + id,
        ["title"] = "Stand-up",
        ["start"] = "2026-11-02T09:00:00",
        ["timeZone"] = "UTC",
        ["duration"] = "PT15M",
        ["recurrenceRules"] = new JsonArray(new JsonObject { ["frequency"] = "daily", ["count"] = 5 }),
        ["participants"] = new JsonObject
        {
            ["o"] = new JsonObject { ["email"] = Me, ["calendarAddress"] = "mailto:" + Me, ["roles"] = new JsonObject { ["owner"] = true }, ["participationStatus"] = "accepted" },
            ["a"] = new JsonObject { ["email"] = "ann@x.example", ["calendarAddress"] = "mailto:ann@x.example", ["roles"] = new JsonObject { ["attendee"] = true }, ["participationStatus"] = "accepted" },
        },
        ["organizerCalendarAddress"] = "mailto:" + Me,
    };

    /// <summary>Waits until an observer hook has been told (observers run in the background).</summary>
    public static async Task<T> HeardAsync<T>(HookBus hooks, string hook, Func<Task> act)
    {
        var heard = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = hooks.Observe<T>(hook, (p, _) =>
        {
            heard.TrySetResult(p);
            return ValueTask.CompletedTask;
        });
        await act();
        return await heard.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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

        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, Account, StringComparison.Ordinal) ? new Session(client) : null;
    }

    private sealed class Session(IJmapClient client) : IAccountSession
    {
        public AccountInfo Account => new(CalendarRig.Account, Me, "example.org");

        public IJmapClient Client => client;

        public IMailService Mail => throw new NotSupportedException();

        public IMailSync Sync => throw new NotSupportedException();
    }
}
