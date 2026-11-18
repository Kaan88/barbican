// Test wiring of the contacts data layer over the fake JMAP server: logins (each a FakeJmapServer with contact data and a connected client on a given clock) behind an IAccountManager, a hook bus that records, can stop an intercept and can reshape a transform, and the push relay.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail;
using Barbican.Core.Sync;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Contacts;

internal sealed class ContactRig : IAccountManager
{
    private readonly List<Login> _logins = [];

    private ContactRig(TimeProvider time)
    {
        Time = time;
        Hooks = new ContactHooks();
        Push = new CalendarPushRelay();
    }

    public TimeProvider Time { get; }

    public ContactHooks Hooks { get; }

    public CalendarPushRelay Push { get; }

    /// <summary>The client traces of every login (push open, state, closed).</summary>
    public List<string> Trace { get; } = [];

    public bool Traced(string text)
    {
        lock (Trace)
        {
            return Trace.Exists(t => t.Contains(text, StringComparison.Ordinal));
        }
    }

    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<AccountInfo> Accounts => [.. _logins.Select(l => l.Info)];

    public AccountInfo? Active => _logins.Count > 0 ? _logins[0].Info : null;

    public static ContactRig Create(TimeProvider? time = null) => new(time ?? TimeProvider.System);

    /// <summary>Signs a login in: a fake server whose contacts are <paramref name="contacts"/> (the capability is on unless the options say otherwise) and a connected client. The key is the account key.</summary>
    public async Task<(string Key, FakeJmapServer Server, JmapClient Client)> AddAsync(string user, FakeContacts? contacts, FakeJmapOptions? options = null, bool readOnly = false, bool capability = true)
    {
        var o = (options ?? new FakeJmapOptions()) with { Username = user, Origin = "https://" + user.Split('@')[0] + ".example.org", Contacts = contacts, ContactsCapability = capability && contacts is not null };
        var server = new FakeJmapServer(o);
        var client = await FakeJmapHarness.ConnectAsync(server, Time, line => { lock (Trace) { Trace.Add(line); } }, readOnly, TestContext.Current.CancellationToken);
        var key = AccountKey.Of(o.Origin, user);
        _logins.Add(new Login(new AccountInfo(key, user, o.Origin), client));
        return (key, server, client);
    }

    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

    public IAccountSession? SessionFor(string accountKey) => _logins.Find(l => string.Equals(l.Info.Key, accountKey, StringComparison.Ordinal)) is { } l ? new Session(l) : null;

    private sealed record Login(AccountInfo Info, JmapClient Client);

    private sealed class Session(Login login) : IAccountSession
    {
        public AccountInfo Account => login.Info;

        public IJmapClient Client => login.Client;

        public IMailService Mail => throw new NotSupportedException();

        public IMailSync Sync => throw new NotSupportedException();
    }
}
