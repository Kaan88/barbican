// Mirrors: Rust main.rs:753 on_login (password, a one-time code appended; remembered only without a code), :769 on_sso_login (OAuth),
// :816 on_switch_account (the current session stays until the new one is up; a failed switch keeps it), :1946 remember_session, :1972
// restore_session (a rejected account is marked, never forgotten), :1968 auth_rejected; Bulwark stores/auth-store.ts:1903 login,
// stores/account-store.ts (several accounts, the first is the default), lib/account-inbox-unread.ts (the popover's unread per account),
// lib/plugin-hook-catalog.ts:276-:281 (onLogin, onAuthReady, onTokenRefresh, onAccountAdd, onAccountSwitch). spec I-005, I-010, B-OAUTH-016,
// B-OAUTH-018.
// Decisions (FABLE-REVIEW "M1 stream: accounts"): every signed-in account keeps its session (switching back is instant); only the active one
// runs its sync engine (MailEventHub attaches to it); the others get a cheap unread watch, one Mailbox/get every two minutes, instead of a
// push stream each. The list is most recently used first (Rust oauth.rs:378); the default is the first account ever added (Bulwark).

using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;

namespace Barbican.Core.Accounts;

/// <summary>The signed-in accounts, sign-in, switching and the account rows.</summary>
public sealed class AccountManager : IAccountManager, IAccountOverview, IAsyncDisposable
{
    /// <summary>How often an inactive account's inbox unread is read.</summary>
    public static readonly TimeSpan UnreadInterval = TimeSpan.FromMinutes(2);

    private readonly IJmapClientFactory _jmap;
    private readonly IMailAccountFactory _mail;
    private readonly AccountStore _store;
    private readonly OAuthFlow _oauth;
    private readonly OAuthHttp _http;
    private readonly IProfilePaths _paths;
    private readonly ReadOnlyPolicy _policy;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly JmapClientOptions _options;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private readonly CancellationTokenSource _life = new();
    private string? _activeKey;
    private ITimer? _unreadTimer;

    public AccountManager(
        IJmapClientFactory jmap,
        IMailAccountFactory mail,
        AccountStore store,
        OAuthFlow oauth,
        OAuthHttp http,
        IProfilePaths paths,
        ReadOnlyPolicy policy,
        IHookBus hooks,
        TimeProvider time,
        JmapClientOptions? options = null)
    {
        _jmap = jmap;
        _mail = mail;
        _store = store;
        _oauth = oauth;
        _http = http;
        _paths = paths;
        _policy = policy;
        _hooks = hooks;
        _time = time;
        _options = options ?? new JmapClientOptions();
    }

    /// <inheritdoc/>
    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged;

    /// <inheritdoc/>
    public event EventHandler? StatusesChanged;

    /// <inheritdoc/>
    public IReadOnlyList<AccountInfo> Accounts
    {
        get
        {
            lock (_lock)
            {
                return [.. _order.Select(k => _entries[k].Info)];
            }
        }
    }

    /// <inheritdoc/>
    public AccountInfo? Active
    {
        get
        {
            lock (_lock)
            {
                return _activeKey is { } k ? _entries[k].Info : null;
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<AccountStatus> Statuses
    {
        get
        {
            lock (_lock)
            {
                return [.. _order.Select(k => _entries[k]).Select(e => new AccountStatus(e.Info, string.Equals(e.Info.Key, _activeKey, StringComparison.Ordinal), e.IsDefault, e.Session is not null, e.Unread, e.Error))];
            }
        }
    }

    /// <summary>
    /// Start-up: reads the remembered accounts, connects the most recent one and makes it active, then connects the others in the background.
    /// Returns the active account, or null when none is remembered or the first one could not connect (its row then carries the error).
    /// </summary>
    public async Task<AccountInfo?> RestoreAsync(CancellationToken ct)
    {
        var stored = await _store.AllAsync(ct).ConfigureAwait(false);
        lock (_lock)
        {
            foreach (var s in stored.Where(s => !_entries.ContainsKey(s.Key)))
            {
                _entries[s.Key] = new Entry(s.ToInfo(_policy.IsReadOnly(s.User, s.ReadOnly))) { Stored = s, IsDefault = s.IsDefault };
                _order.Add(s.Key);
            }
        }

        StartUnreadWatch();
        Changed();
        if (stored.Count == 0)
        {
            return null;
        }

        var first = await ConnectAsync(stored[0].Key, ct).ConfigureAwait(false);
        if (first.IsOk)
        {
            await ActivateAsync(stored[0].Key, ct).ConfigureAwait(false);
        }

        foreach (var other in stored.Skip(1))
        {
            _ = ConnectQuietlyAsync(other.Key);
        }

        return first.IsOk ? Active : null;
    }

    /// <inheritdoc/>
    public async Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = ServerEndpoint.Read(request.Server);
        if (!read.IsOk)
        {
            return Result<AccountInfo>.Fail(read.Error!);
        }

        var endpoint = read.Value!;

        var user = request.User.Trim();
        if (user.Length == 0 || request.Password.Length == 0)
        {
            return Result<AccountInfo>.Fail(new AuthError("Enter your username and password.", NeedsSignIn: false));
        }

        var code = string.IsNullOrWhiteSpace(request.TotpCode) ? null : request.TotpCode.Trim();
        var source = StaticAccessTokenSource.Basic(user, request.Password, code);
        var stored = new StoredAccount(StoredAccount.PasswordKind, Base(endpoint), user) { Password = request.Password };
        var connected = await _jmap.ConnectAsync(endpoint, source, OptionsFor(stored), ct).ConfigureAwait(false);
        if (!connected.IsOk)
        {
            return Result<AccountInfo>.Fail(connected.Error!);
        }

        // Rust main.rs:762: a one-time code is never remembered, and with one the session is not remembered either
        return Result<AccountInfo>.Ok(await AddAsync(stored, connected.Value!, null, request.Remember && code is null, ct).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = ServerEndpoint.Read(request.Server);
        if (!read.IsOk)
        {
            return Result<AccountInfo>.Fail(string.IsNullOrWhiteSpace(request.Server) ? new AuthError("Enter your server address first.", NeedsSignIn: false) : read.Error!);
        }

        var endpoint = read.Value!;

        var hint = request.LoginHint?.Trim() ?? "";
        var tokens = await _oauth.SignInAsync(endpoint.BaseUri, hint, request.ClientId ?? "", ct).ConfigureAwait(false);
        if (!tokens.IsOk)
        {
            return Result<AccountInfo>.Fail(tokens.Error!);
        }

        var source = new OAuthTokenSource(_http, _store, _paths, _time, AccountKey.Of(Base(endpoint), hint), tokens.Value!);
        var connected = await _jmap.ConnectAsync(endpoint, source, OptionsFor(new StoredAccount(StoredAccount.OAuthKind, Base(endpoint), hint)), ct).ConfigureAwait(false);
        if (!connected.IsOk)
        {
            source.Dispose();
            return Result<AccountInfo>.Fail(connected.Error!);
        }

        var user = connected.Value!.Session.Username is { Length: > 0 } u ? u : hint;

        // the client was opened before the login was known (the hint may be empty or another name): when the real login must be read-only
        // and the client is not, connect again read-only rather than hand out a writable client for it (PORT-PLAN.md §4.6)
        if (!connected.Value!.IsReadOnly && _policy.IsReadOnly(user, storedFlag: false))
        {
            connected = await _jmap.ConnectAsync(endpoint, source, _options with { ReadOnly = true }, ct).ConfigureAwait(false);
            if (!connected.IsOk)
            {
                source.Dispose();
                return Result<AccountInfo>.Fail(connected.Error!);
            }
        }

        var t = source.Tokens;
        var stored = new StoredAccount(StoredAccount.OAuthKind, Base(endpoint), user) { Refresh = t.Refresh, Endpoint = t.TokenEndpoint.AbsoluteUri, Client = t.ClientId };
        source.BindAccount(stored.Key);
        return Result<AccountInfo>.Ok(await AddAsync(stored, connected.Value!, source, request.Remember && t.Refresh is not null, ct).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        string user;
        lock (_lock)
        {
            if (string.Equals(accountKey, _activeKey, StringComparison.Ordinal))
            {
                return Result<Unit>.Ok(default);
            }

            if (!_entries.TryGetValue(accountKey, out var e))
            {
                return Result<Unit>.Fail(new AuthError("No such account.", NeedsSignIn: false));
            }

            user = e.Info.User;
        }

        // the current account stays on screen until the new one is up; a failed switch keeps it (Rust main.rs:820)
        var connected = await ConnectAsync(accountKey, ct).ConfigureAwait(false);
        if (!connected.IsOk)
        {
            return Result<Unit>.Fail(new AuthError($"Could not switch to {user}: {connected.Error!.Message}", connected.Error is AuthError { NeedsSignIn: true }));
        }

        await ActivateAsync(accountKey, ct).ConfigureAwait(false);
        await _store.TouchAsync(accountKey, ct).ConfigureAwait(false);
        return Result<Unit>.Ok(default);
    }

    /// <inheritdoc/>
    public IAccountSession? SessionFor(string accountKey)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(accountKey, out var e) ? e.Session : null;
        }
    }

    /// <summary>Reads the inbox unread of every connected account that is not on screen (the timer does this every two minutes).</summary>
    public async Task RefreshUnreadAsync(CancellationToken ct)
    {
        List<(string Key, AccountSession Session)> todo;
        lock (_lock)
        {
            todo = [.. _entries.Values.Where(e => e.Session is not null && !string.Equals(e.Info.Key, _activeKey, StringComparison.Ordinal)).Select(e => (e.Info.Key, e.Session!))];
        }

        foreach (var (key, session) in todo)
        {
            await RefreshUnreadOfAsync(key, session, ct).ConfigureAwait(false);
        }
    }

    private int _disposed;

    public async ValueTask DisposeAsync()
    {
        // the container disposes this one instance once for every service type it was registered under (AccountManager, IAccountManager, IAccountOverview)
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _life.CancelAsync().ConfigureAwait(false);
        List<AccountSession> sessions;
        lock (_lock)
        {
            _unreadTimer?.Dispose();
            _unreadTimer = null;
            sessions = [.. _entries.Values.Select(e => e.Session).OfType<AccountSession>()];
        }

        foreach (var s in sessions)
        {
            s.Dispose();
        }

        _life.Dispose();
    }

    private static string Base(ServerEndpoint endpoint) => endpoint.BaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');

    private static string Describe(JmapError error, string user) => error is AuthError { NeedsSignIn: true } || (error is AuthError && OAuthRefresh.IsRejection(error.Message))
        ? $"{user} needs to sign in again."
        : error.Message;

    private JmapClientOptions OptionsFor(StoredAccount account) => _options with { ReadOnly = _policy.IsReadOnly(account.User, account.ReadOnly) };

    private async Task<AccountInfo> AddAsync(StoredAccount stored, IJmapClient client, OAuthTokenSource? tokens, bool remember, CancellationToken ct)
    {
        var info = stored.ToInfo(client.IsReadOnly);
        var service = _mail.CreateService(client);
        var session = new AccountSession(info, client, service, _mail.CreateSync(service), tokens);
        Watch(tokens);
        AccountSession? replaced;
        bool added;
        lock (_lock)
        {
            added = !_entries.TryGetValue(info.Key, out var e);
            e ??= new Entry(info) { IsDefault = _entries.Values.All(x => !x.IsDefault) };
            replaced = e.Session;
            e.Info = info;
            e.Session = session;
            e.Error = null;
            e.Stored = stored;
            _entries[info.Key] = e;
            _order.Remove(info.Key);
            _order.Insert(0, info.Key);
        }

        replaced?.Dispose();
        if (remember)
        {
            await _store.RememberAsync(stored, ct).ConfigureAwait(false);
        }

        await NotifyAsync(HookNames.OnLogin, new LoginHookPayload(info.Key, info.User), ct).ConfigureAwait(false);
        await NotifyAsync(HookNames.OnAuthReady, new AccountHookPayload(info.Key), ct).ConfigureAwait(false);
        if (added && Accounts.Count > 1)
        {
            await NotifyAsync(HookNames.OnAccountAdd, new AccountHookPayload(info.Key), ct).ConfigureAwait(false);
        }

        StartUnreadWatch();
        await ActivateAsync(info.Key, ct).ConfigureAwait(false);
        _ = RefreshUnreadOfAsync(info.Key, session, _life.Token);
        return info;
    }

    // connects a known account once; concurrent callers share the attempt
    private Task<Result<AccountSession>> ConnectAsync(string key, CancellationToken ct)
    {
        Entry e;
        lock (_lock)
        {
            e = _entries[key];
            if (e.Session is { } s)
            {
                return Task.FromResult(Result<AccountSession>.Ok(s));
            }

            if (e.Connecting is { } pending)
            {
                return pending.WaitAsync(ct);
            }

            if (e.Stored is null)
            {
                return Task.FromResult(Result<AccountSession>.Fail(new AuthError($"{e.Info.User} needs to sign in again.", NeedsSignIn: true)));
            }

            // a connect that finished synchronously already ran its finally (this lock is re-entrant): keeping the task would pin its result
            // and the account could never be retried
            var attempt = ConnectStoredAsync(e, e.Stored, _life.Token);
            e.Connecting = attempt.IsCompleted ? null : attempt;
            return attempt.WaitAsync(ct);
        }
    }

    private async Task<Result<AccountSession>> ConnectStoredAsync(Entry e, StoredAccount stored, CancellationToken ct)
    {
        try
        {
            var read = ServerEndpoint.Read(stored.Server);
            if (!read.IsOk)
            {
                return Fail(new AuthError($"The remembered server address cannot be used. {read.Error!.Message}", NeedsSignIn: true));
            }

            var endpoint = read.Value!;

            if (SourceFor(stored) is not var (source, oauth))
            {
                return Fail(new AuthError($"{OAuthTokenSource.NeedsSignInPrefix}; sign in again.", NeedsSignIn: true));
            }

            var connected = await _jmap.ConnectAsync(endpoint, source, OptionsFor(stored), ct).ConfigureAwait(false);
            if (!connected.IsOk)
            {
                oauth?.Dispose();
                return Fail(connected.Error!);
            }

            var client = connected.Value!;
            var info = stored.ToInfo(client.IsReadOnly);
            var service = _mail.CreateService(client);
            var session = new AccountSession(info, client, service, _mail.CreateSync(service), oauth);
            Watch(oauth);
            lock (_lock)
            {
                e.Info = info;
                e.Session = session;
                e.Error = null;
            }

            Changed();
            await NotifyAsync(HookNames.OnAuthReady, new AccountHookPayload(info.Key), ct).ConfigureAwait(false);
            _ = RefreshUnreadOfAsync(info.Key, session, _life.Token);
            return Result<AccountSession>.Ok(session);
        }
        finally
        {
            lock (_lock)
            {
                e.Connecting = null;
            }
        }

        Result<AccountSession> Fail(JmapError error)
        {
            // a rejected account is only marked; its remembered entry stays (Rust main.rs:1993, B-OAUTH-016)
            lock (_lock)
            {
                e.Error = Describe(error, stored.User);
            }

            Changed();
            return Result<AccountSession>.Fail(error);
        }
    }

    // the remembered secret as a token source; null for an OAuth entry without a usable refresh token
    private (IAccessTokenSource Source, OAuthTokenSource? OAuth)? SourceFor(StoredAccount stored)
    {
        if (!stored.IsOAuth)
        {
            return (StaticAccessTokenSource.Basic(stored.User, stored.Password ?? ""), null);
        }

        if (stored.Refresh is null || stored.Endpoint is null || !Uri.TryCreate(stored.Endpoint, UriKind.Absolute, out var tokenEndpoint))
        {
            return null;
        }

        var oauth = new OAuthTokenSource(_http, _store, _paths, _time, stored.Key, new OAuthTokens("", stored.Refresh, null, tokenEndpoint, stored.Client ?? OAuthClientRegistry.DefaultClientId));
        return (oauth, oauth);
    }

    private async Task ConnectQuietlyAsync(string key)
    {
        try
        {
            _ = await ConnectAsync(key, _life.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task ActivateAsync(string key, CancellationToken ct)
    {
        AccountInfo? previous;
        AccountInfo current;
        lock (_lock)
        {
            previous = _activeKey is { } p ? _entries[p].Info : null;
            current = _entries[key].Info;
            _activeKey = key;
        }

        Changed();
        if (previous is not null && string.Equals(previous.Key, key, StringComparison.Ordinal))
        {
            return;
        }

        ActiveChanged?.Invoke(this, new ActiveAccountChangedEventArgs(previous, current));
        if (previous is not null)
        {
            await NotifyAsync(HookNames.OnAccountSwitch, new AccountSwitchHookPayload(key, previous.Key), ct).ConfigureAwait(false);
            if (SessionFor(previous.Key) is AccountSession left)
            {
                _ = RefreshUnreadOfAsync(previous.Key, left, _life.Token);
            }
        }
    }

    private async Task RefreshUnreadOfAsync(string key, AccountSession session, CancellationToken ct)
    {
        try
        {
            var tree = await session.Mail.GetMailboxesAsync(ct).ConfigureAwait(false);
            if (!tree.IsOk)
            {
                return;
            }

            var unread = InboxUnread.Of(tree.Value);
            bool changed;
            lock (_lock)
            {
                changed = _entries.TryGetValue(key, out var e) && ReferenceEquals(e.Session, session) && e.Unread != unread;
                if (changed)
                {
                    e!.Unread = unread;
                }
            }

            if (changed)
            {
                Changed();
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private void StartUnreadWatch()
    {
        lock (_lock)
        {
            _unreadTimer ??= _time.CreateTimer(_ => _ = RefreshUnreadAsync(_life.Token), null, UnreadInterval, UnreadInterval);
        }
    }

    private void Watch(OAuthTokenSource? tokens)
    {
        if (tokens is not null)
        {
            tokens.Refreshed += (_, _) => _ = NotifyAsync(HookNames.OnTokenRefresh, new AccountHookPayload(tokens.AccountKey), _life.Token).AsTask();
        }
    }

    private async ValueTask NotifyAsync<T>(string hook, T payload, CancellationToken ct)
    {
        if (_hooks.HasHandlers(hook))
        {
            await _hooks.NotifyAsync(hook, payload, ct).ConfigureAwait(false);
        }
    }

    private void Changed() => StatusesChanged?.Invoke(this, EventArgs.Empty);

    private sealed class Entry(AccountInfo info)
    {
        public AccountInfo Info { get; set; } = info;

        public StoredAccount? Stored { get; set; }

        public AccountSession? Session { get; set; }

        public Task<Result<AccountSession>>? Connecting { get; set; }

        public bool IsDefault { get; set; }

        public int? Unread { get; set; }

        public string? Error { get; set; }
    }
}
