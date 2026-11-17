// M1 hotfix (sign-in), HAND TEST 1: the browser sign-in worked up to the loopback callback, then the app died with a stowed WinRT exception
// (CoreMessagingXP.dll 0xc000027b, combase RPC_E_WRONG_THREAD), and every later start showed an empty mail screen. Cause: the account manager
// finishes a sign-in (and the start-up restore) on a thread-pool continuation and raises ActiveChanged there; ActiveMailSource passed it on
// synchronously to ViewModels bound to the UI (the message list's rows, the rail's labels, MailShellStartup's loads). These tests run the
// real sign-in on FakeJmap with the fake OAuth provider, the callback delivered from the thread pool, under a UI-thread stand-in whose bound
// subscribers throw off the UI thread exactly as WinUI does. Rust main.rs:769 on_sso_login / :1972 restore_session finish on the UI thread
// through slint::invoke_from_event_loop; Bulwark has one thread.

using System.Collections.Specialized;
using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Tests.Accounts;
using Barbican.Core.Tests.Auth;
using Barbican.Core.Tests.Settings.Prefs;
using Barbican.FakeJmap;
using Barbican.Presentation.Accounts;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Accounts;

public sealed class SignInThreadTests : IAsyncDisposable
{
    private const string Origin = "https://jmap.example.org";
    private const string User = "ana@example.org";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "barbican-signin-" + Guid.NewGuid().ToString("N"));
    private readonly FakeJmapServer _server;
    private readonly FakeOAuthProvider _provider;
    private readonly JmapHttp _http;
    private readonly MemoryVault _vault = new();
    private readonly UiThread _ui = new();
    private readonly List<string> _wrongThread = [];
    private readonly List<IAsyncDisposable> _managers = [];
    private readonly List<IDisposable> _owned = [];

    public SignInThreadTests()
    {
        _server = new FakeJmapServer(new FakeJmapOptions { Origin = Origin, Username = User, Password = "pw", Otp = null });
        _provider = new FakeOAuthProvider(_server) { StalwartLike = true };
        _http = new JmapHttp(() => _provider);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (var m in _managers)
        {
            await m.DisposeAsync();
        }

        foreach (var d in _owned)
        {
            d.Dispose();
        }

        _ui.Dispose();
        _http.Dispose();
        _provider.Dispose();
        _server.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    // the system browser's part, from the thread pool: GET the authorize URL, deliver the redirect to the loopback
    private sealed class ThreadPoolBrowser(IAuthorizationLauncher inner) : IAuthorizationLauncher
    {
        public Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct) => Task.Run(() => inner.OpenAsync(authorizeUri, ct), ct);
    }

    private sealed class FailingVault : IAccountVault
    {
        public Task<Result<IReadOnlyList<StoredAccount>>> LoadAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<StoredAccount>>.Fail(new Barbican.Core.Jmap.Errors.TransportError("the store is locked", Retryable: false)));

        public Task<Result<Unit>> SaveAsync(IReadOnlyList<StoredAccount> accounts, CancellationToken ct) =>
            Task.FromResult(Result<Unit>.Fail(new Barbican.Core.Jmap.Errors.TransportError("the store is locked", Retryable: false)));
    }

    private AccountManager NewManager(IAccountVault? vault = null, JmapHttp? over = null)
    {
        var http = over ?? _http;
        var time = TimeProvider.System;
        var paths = new TestProfilePaths(_root);
        var oauthHttp = new OAuthHttp(http, time);
        var clients = new OAuthClientRegistry(oauthHttp, paths);
        var store = new AccountStore(vault ?? _vault);
        _owned.Add(clients);
        _owned.Add(store);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), clients, oauthHttp, new ThreadPoolBrowser(new RedirectFollowingLauncher(http)), new OAuthFlowOptions { LoopbackPort = 0, Timeout = TimeSpan.FromSeconds(20) }, time);
        var hooks = new HookBus(time);
        var m = new AccountManager(new JmapClientFactory(http, time), new MailAccountFactory(hooks, time), store, flow, oauthHttp, paths, ReadOnlyPolicy.AllReadOnly, hooks, time);
        _managers.Add(m);
        return m;
    }

    // what a WinUI binding does with a change raised off its thread: RPC_E_WRONG_THREAD, thrown back at the raiser
    private void OnUi(string what)
    {
        if (!_ui.HasThreadAccess)
        {
            lock (_wrongThread)
            {
                _wrongThread.Add(what);
            }

            throw new InvalidOperationException($"RPC_E_WRONG_THREAD stand-in: {what} raised off the UI thread");
        }
    }

    // the UI-bound side of the app over one manager: the mail source with a bound subscriber, the account popover, the sign-in form
    private (ActiveMailSource Mail, AccountsViewModel Popover, SignInViewModel Form, Func<int> MailChanges) Bind(AccountManager manager)
    {
        var mail = new ActiveMailSource(manager, _ui);
        var changes = 0;
        mail.Changed += (_, _) =>
        {
            OnUi("IActiveMail.Changed (message list rows, rail labels)");
            changes++;
        };
        var popover = new AccountsViewModel(manager, manager, _ui);
        popover.PropertyChanged += (_, e) => OnUi("AccountsViewModel." + e.PropertyName);
        ((INotifyCollectionChanged)popover.Rows).CollectionChanged += (_, _) => OnUi("AccountsViewModel.Rows");
        var form = new SignInViewModel(manager, new ServerDiscovery(_http, TimeProvider.System), _ui);
        form.PropertyChanged += (_, e) => OnUi("SignInViewModel." + e.PropertyName);
        form.SignedIn += (_, _) => OnUi("SignInViewModel.SignedIn");
        _owned.Add(mail);
        _owned.Add(popover);
        _owned.Add(form);
        return (mail, popover, form, () => Volatile.Read(ref changes));
    }

    private Task OnUiAsync(Action<SignInViewModel> fill, SignInViewModel form, Func<SignInViewModel, Task> run) =>
        _ui.InvokeAsync(() =>
        {
            fill(form);
            return run(form);
        });

    private void AssertNothingOffTheUiThread()
    {
        lock (_wrongThread)
        {
            Assert.Empty(_wrongThread);
        }

        Assert.Empty(_ui.Unhandled);
    }

    [Fact]
    public async Task A_browser_sign_in_finishes_on_the_UI_thread_and_is_remembered()
    {
        var manager = NewManager();
        var (mail, _, form, mailChanges) = Bind(manager);
        var signedIn = 0;
        form.SignedIn += (_, _) => signedIn++;

        await OnUiAsync(f => { f.Server = Origin; f.User = User; }, form, f => f.OAuthSignInCommand.ExecuteAsync(null));
        await _ui.FlushAsync();

        AssertNothingOffTheUiThread();
        Assert.True(signedIn == 1, form.Status);
        Assert.Equal(User, mail.AccountUser);
        Assert.True(mailChanges() >= 1);
        Assert.Equal(User, Assert.Single(_vault.Accounts).User);

        // the account hint went out as the Rust app sends it (discovery and login_hint)
        Assert.Equal(User, Assert.Single(_provider.LoginHints));
    }

    [Fact]
    public async Task A_password_and_one_time_code_sign_in_finishes_on_the_UI_thread()
    {
        using var otp = new FakeJmapServer(new FakeJmapOptions { Origin = Origin, Username = User, Password = "pw", Otp = "123456" });
        using var otpHttp = new JmapHttp(() => otp);
        var manager = NewManager(over: otpHttp);
        var (mail, _, form, _) = Bind(manager);

        await OnUiAsync(f => { f.Server = Origin; f.User = User; f.Password = "pw"; }, form, f => f.SignInCommand.ExecuteAsync(null));
        Assert.True(form.NeedsTotp, form.Status);
        await OnUiAsync(f => f.TotpCode = "123456", form, f => f.SignInCommand.ExecuteAsync(null));
        await _ui.FlushAsync();

        AssertNothingOffTheUiThread();
        Assert.Equal(User, mail.AccountUser);
    }

    [Fact]
    public async Task Every_way_a_sign_in_ends_reaches_the_form_on_the_UI_thread()
    {
        var manager = NewManager();
        var (_, _, form, _) = Bind(manager);

        // refused at the provider, a wrong password, an unknown server: each ends with a status line set on the UI thread
        _provider.NextError = "access_denied";
        await OnUiAsync(f => { f.Server = Origin; f.User = User; }, form, f => f.OAuthSignInCommand.ExecuteAsync(null));
        Assert.True(form.StatusIsError);
        await OnUiAsync(f => f.Password = "wrong", form, f => f.SignInCommand.ExecuteAsync(null));
        Assert.True(form.StatusIsError);
        await OnUiAsync(f => { f.Server = "https://nowhere.example.net"; f.Password = "pw"; }, form, f => f.SignInCommand.ExecuteAsync(null));
        Assert.True(form.StatusIsError);

        // and the successful ones
        await OnUiAsync(f => f.Server = Origin, form, f => f.SignInCommand.ExecuteAsync(null));
        await OnUiAsync(_ => { }, form, f => f.OAuthSignInCommand.ExecuteAsync(null));
        await _ui.FlushAsync();
        AssertNothingOffTheUiThread();
        Assert.NotNull(manager.Active);
    }

    [Fact]
    public async Task Restoring_a_remembered_account_tells_the_UI_on_the_UI_thread()
    {
        Assert.True((await NewManager().SignInWithPasswordAsync(new PasswordSignIn(Origin, User, "pw", null, true), Ct)).IsOk);

        // the next start: AccountsStartup restores on the thread pool while the shell is bound
        var manager = NewManager();
        var (mail, popover, _, mailChanges) = Bind(manager);
        var restored = await Task.Run(() => manager.RestoreAsync(Ct), Ct);
        await _ui.FlushAsync();

        AssertNothingOffTheUiThread();
        Assert.Equal(User, restored?.User);
        Assert.Equal(User, mail.AccountUser);
        Assert.True(mailChanges() >= 1);
        Assert.Single(popover.Rows);
    }

    [Fact]
    public async Task A_finished_sign_in_is_remembered_before_anything_can_fail_on_it()
    {
        // the crash of HAND TEST 1 happened when the new account was announced; the account must already be stored by then
        var manager = NewManager();
        manager.ActiveChanged += (_, _) => throw new InvalidOperationException("the app died here");
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SignInWithOAuthAsync(new OAuthSignIn(Origin, null, Remember: true) { LoginHint = User }, Ct));
        Assert.Equal(User, Assert.Single(_vault.Accounts).User);

        // the next start finds it and signs it in without the form
        var next = NewManager();
        var restored = await Task.Run(() => next.RestoreAsync(Ct), Ct);
        Assert.Equal(User, restored?.User);
        Assert.NotNull(next.SessionFor(restored!.Key));
    }

    [Fact]
    public async Task With_no_remembered_account_the_start_ends_with_nobody_active()
    {
        // the shell shows the sign-in form when the restore is done and nobody is active (ShellPage.ShowScreen), also when the store fails
        foreach (var vault in new IAccountVault[] { new MemoryVault(), new FailingVault() })
        {
            var manager = NewManager(vault);
            var (mail, _, _, mailChanges) = Bind(manager);
            Assert.Null(await Task.Run(() => manager.RestoreAsync(Ct), Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct));
            await _ui.FlushAsync();
            Assert.Null(manager.Active);
            Assert.Null(mail.AccountKey);
            Assert.Equal(0, mailChanges());
        }

        AssertNothingOffTheUiThread();
    }

    [Fact]
    public async Task A_change_raised_on_the_UI_thread_is_delivered_at_once()
    {
        var manager = NewManager();
        var (_, _, _, mailChanges) = Bind(manager);
        await _ui.InvokeAsync(async () =>
        {
            Assert.True((await manager.SignInWithPasswordAsync(new PasswordSignIn(Origin, User, "pw", null, false), Ct)).IsOk);
        });
        await _ui.FlushAsync();
        Assert.True(mailChanges() >= 1);
        AssertNothingOffTheUiThread();
    }
}
