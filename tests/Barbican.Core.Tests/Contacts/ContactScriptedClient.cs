// A JMAP client that answers from a script, for the calls the fake server has no arm for (Principal/get, the refusal of Principal/query): the contact directory tests.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;

namespace Barbican.Core.Tests.Contacts;

internal sealed class ContactScriptedClient(Func<JmapRequest, JmapResponse> script, IReadOnlyList<string> capabilities, int maxGet = 500) : IJmapClient, IAccountManager
{
    private readonly List<JmapRequest> _requests = [];

    /// <summary>Every request that arrived.</summary>
    public IReadOnlyList<JmapRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public JmapSession Session { get; } = new()
    {
        CapabilityNames = capabilities,
        PrimaryAccounts = capabilities.ToDictionary(c => c, _ => "acc", StringComparer.Ordinal),
        Limits = new CoreLimits { MaxObjectsInGet = maxGet },
    };

    public bool IsReadOnly => false;

    public const string Key = "scripted";

    public Task<Result<JmapResponse>> SendAsync(JmapRequest request, CancellationToken ct)
    {
        lock (_requests)
        {
            _requests.Add(request);
        }

        return Task.FromResult(Result<JmapResponse>.Ok(script(request)));
    }

    public Task<Result<Stream>> DownloadAsync(string accountId, string blobId, string name, string type, CancellationToken ct) => throw new NotSupportedException();

    public T? AccountCapability<T>(string accountId, string capability)
        where T : class => null;

    public IReadOnlyList<AccountInfo> Accounts => [new(Key, "user@example.org", "https://x.example")];

    public AccountInfo? Active => Accounts[0];

    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
    {
        add { }
        remove { }
    }

    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

    public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, Key, StringComparison.Ordinal) ? new LoginSession(this) : null;

    private sealed class LoginSession(ContactScriptedClient owner) : IAccountSession
    {
        public AccountInfo Account => owner.Accounts[0];

        public IJmapClient Client => owner;

        public IMailService Mail => throw new NotSupportedException();

        public IMailSync Sync => throw new NotSupportedException();
    }
}
