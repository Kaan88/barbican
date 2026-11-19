// The srv-data services over the fake JMAP server: a signed-in account on a connected client, a recording hook bus, the real services of Filters.Rules, Settings.Vacation, Settings.Folders and Settings.Templates. M6 stream srv-data; no other stream's test folder uses this name.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Compose;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Folders;
using Barbican.Core.Settings.Templates;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Filters.Rules;

internal sealed class FilterTestRig
{
    public const string Account = "acct";

    private FilterTestRig(Fake fake, FakeTimeProvider clock)
    {
        Fake = fake;
        Clock = clock;
        Hooks = new HookBus(clock);
        var accounts = new FakeAccounts(fake.Client, clock);
        Filters = new FilterService(accounts, Hooks);
        var calendars = new CalendarStore(accounts, Hooks);
        var writer = new CalendarWriter(accounts, calendars, Hooks);
        Vacation = new VacationService(accounts, Hooks, clock, calendars, writer);
        Folders = new FolderAdmin(accounts, clock);
        Templates = new TemplateAdmin(accounts, new TemplateStore(accounts, clock), Hooks, clock);
    }

    public Fake Fake { get; }

    public FakeTimeProvider Clock { get; }

    public HookBus Hooks { get; }

    public FilterService Filters { get; }

    public VacationService Vacation { get; }

    public FolderAdmin Folders { get; }

    public TemplateAdmin Templates { get; }

    public FakeStore Store => Fake.Server.Store;

    public FakeApi Api => Fake.Server.Api;

    public static async Task<FilterTestRig> StartAsync(FakeJmapOptions? options = null, bool readOnly = false)
    {
        var clock = Fake.VirtualClock();
        var o = (options ?? new FakeJmapOptions { SieveCapability = true, VacationCapability = true, MetadataCapability = true }) with { Clock = clock };
        return new FilterTestRig(await Fake.StartAsync(o, clock, readOnly), clock);
    }

    /// <summary>The ids of the messages in a folder.</summary>
    public IReadOnlyList<string> InFolder(string mailboxId) =>
        [.. Store.EmailIds().Where(id => Store.Email(id)!["mailboxIds"] is System.Text.Json.Nodes.JsonObject m && m.ContainsKey(mailboxId))];

    private sealed class FakeAccounts(IJmapClient client, TimeProvider time) : IAccountManager
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

        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, Account, StringComparison.Ordinal) ? new Session(client, time) : null;
    }

    private sealed class Session(IJmapClient client, TimeProvider time) : IAccountSession
    {
        public AccountInfo Account => new(FilterTestRig.Account, "user", "example.org");

        public IJmapClient Client => client;

        public IMailService Mail { get; } = new Barbican.Core.Mail.Service.MailService(client, time);

        public IMailSync Sync => throw new NotSupportedException();
    }
}
