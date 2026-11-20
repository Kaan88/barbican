using Barbican.Core.Accounts;
using Barbican.Core.Auth;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Mail.Service;
using Barbican.Core.Tests.Settings.Prefs;

namespace Barbican.Core.Tests.Accounts;

public sealed partial class AccountManagerTests
{
    private readonly BlockingSignOutCleanup _signOutCleanup = new();

    [Fact]
    public async Task SignOutFencesOnlyTargetUntilCleanupSucceeds()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        var source = new PluginSessionSource(_manager);
        var target = source.CaptureOwner(a.Key).Value!;
        var other = source.CaptureOwner(b.Key).Value!;
        _signOutCleanup.Block = true;
        var signOut = _manager.SignOutAsync(a.Key, Ct);
        try
        {
            var call = await _signOutCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.Equal([a.Key], call.Keys);
            Assert.False(call.All);
            Assert.Contains(HookNames.OnBeforeLogout, HooksFor(a.Key), StringComparer.Ordinal);
            Assert.DoesNotContain(HookNames.OnAfterLogout, HooksFor(a.Key), StringComparer.Ordinal);
            Assert.NotNull(_manager.SessionFor(a.Key));
            Assert.Contains(_vault.Accounts, account => string.Equals(account.Key, a.Key, StringComparison.Ordinal));
            Assert.False(source.IsCurrent(target));
            Assert.False(source.CaptureOwner(a.Key).IsOk);
            Assert.True(source.IsCurrent(other));
            Assert.True(source.CaptureOwner(b.Key).IsOk);
            Assert.False(source.TryRunWhileCurrent([target], () => throw new InvalidOperationException("Fenced account posted.")));
        }
        finally { _signOutCleanup.Release.TrySetResult(Result<Unit>.Ok(default)); }
        Assert.True((await signOut).IsOk);
        Assert.Null(_manager.SessionFor(a.Key));
        Assert.DoesNotContain(_vault.Accounts, account => string.Equals(account.Key, a.Key, StringComparison.Ordinal));
        Assert.True(source.IsCurrent(other));
    }

    [Fact]
    public async Task FailedCleanupPreservesAccountAndReleasesFence()
    {
        var a = (await SignInAAsync()).Value!;
        var source = new PluginSessionSource(_manager);
        _signOutCleanup.Block = true;
        var signOut = _manager.SignOutAsync(a.Key, Ct);
        try
        {
            await _signOutCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.False(source.CaptureOwner(a.Key).IsOk);
        }
        finally { _signOutCleanup.Release.TrySetResult(Result<Unit>.Fail(new AuthError("Cleanup failed.", NeedsSignIn: false))); }
        Assert.False((await signOut).IsOk);
        Assert.NotNull(_manager.SessionFor(a.Key));
        Assert.Contains(_vault.Accounts, account => string.Equals(account.Key, a.Key, StringComparison.Ordinal));
        Assert.True(source.CaptureOwner(a.Key).IsOk);
        Assert.DoesNotContain(HookNames.OnAfterLogout, HooksFor(a.Key), StringComparer.Ordinal);
    }

    [Fact]
    public async Task SignOutAllRequestsFullCleanupEvenWithNoAccounts()
    {
        _signOutCleanup.Block = true;
        var signOut = _manager.SignOutAllAsync(Ct);
        try
        {
            var call = await _signOutCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.True(call.All);
            Assert.Empty(call.Keys);
        }
        finally { _signOutCleanup.Release.TrySetResult(Result<Unit>.Ok(default)); }
        Assert.True((await signOut).IsOk);
    }

    [Fact]
    public async Task SigningOutTheLastLoginErasesTheEntireProfile()
    {
        var a = (await SignInAAsync()).Value!;
        _signOutCleanup.Block = true;
        var signOut = _manager.SignOutAsync(a.Key, Ct);
        try
        {
            var request = await _signOutCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.True(request.All);
            Assert.Equal(a.Key, Assert.Single(request.Keys));
        }
        finally { _signOutCleanup.Release.TrySetResult(Result<Unit>.Ok(default)); }
        Assert.True((await signOut).IsOk);
    }

    [Fact]
    public async Task QueuedSignOutCannotDetachAReplacementWithTheSameKey()
    {
        var a = (await SignInAAsync()).Value!;
        var prior = _manager.SessionFor(a.Key);
        _signOutCleanup.Block = true;
        var first = _manager.SignOutAsync(a.Key, Ct);
        await _signOutCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var signBackIn = SignInAAsync();
        var duplicate = _manager.SignOutAsync(a.Key, Ct);
        _signOutCleanup.Release.TrySetResult(Result<Unit>.Ok(default));

        Assert.True((await first).IsOk);
        Assert.True((await signBackIn).IsOk);
        Assert.False((await duplicate).IsOk);
        Assert.NotNull(_manager.SessionFor(a.Key));
        Assert.NotSame(prior, _manager.SessionFor(a.Key));
        Assert.Equal(a.Key, Assert.Single(_vault.Accounts).Key);
    }

    [Fact]
    public async Task ANewLoginPublishesAfterFullErasureCompletes()
    {
        var a = (await SignInAAsync()).Value!;
        _signOutCleanup.Block = true;
        var signOut = _manager.SignOutAllAsync(Ct);
        await _signOutCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        var add = SignInBAsync();
        try
        {
            Assert.False(add.IsCompleted);
            Assert.Equal(a.Key, Assert.Single(_manager.Accounts).Key);
            Assert.Equal(a.Key, Assert.Single(_vault.Accounts).Key);
        }
        finally { _signOutCleanup.Release.TrySetResult(Result<Unit>.Ok(default)); }

        Assert.True((await signOut).IsOk);
        var b = (await add).Value!;
        Assert.Equal(b.Key, Assert.Single(_manager.Accounts).Key);
        Assert.Equal(b.Key, Assert.Single(_vault.Accounts).Key);
    }

    [Fact]
    public async Task ASessionRestoredWithoutTheLeaseCannotGainStorageAfterLeaseHandoff()
    {
        _signOutCleanup.HasStorageLease = true;
        var a = (await SignInAAsync()).Value!;
        Assert.True(_manager.SessionFor(a.Key)!.AllowsAddOnStorage);

        using var otherStore = new AccountStore(_vault);
        var otherCleanup = new BlockingSignOutCleanup { HasStorageLease = false };
        await using var other = CreateSiblingManager(otherStore, otherCleanup);
        Assert.Equal(a.Key, (await other.RestoreAsync(Ct))!.Key);
        var stale = other.SessionFor(a.Key)!;
        Assert.False(stale.AllowsAddOnStorage);

        Assert.True((await _manager.SignOutAsync(a.Key, Ct)).IsOk);
        otherCleanup.HasStorageLease = true;
        await other.RestoreAsync(Ct);
        Assert.Same(stale, other.SessionFor(a.Key));
        Assert.False(stale.AllowsAddOnStorage);

        var fresh = await other.SignInWithPasswordAsync(new PasswordSignIn(OriginA, a.User, "pw-a", null, true), Ct);
        Assert.True(fresh.IsOk);
        Assert.True(other.SessionFor(a.Key)!.AllowsAddOnStorage);
        Assert.NotSame(stale, other.SessionFor(a.Key));
    }

    [Fact]
    public async Task ANewLeaseOwnerDoesNotRestoreAnAccountFromItsStaleVaultCache()
    {
        var a = (await SignInAAsync()).Value!;
        using var otherStore = new AccountStore(_vault);
        Assert.Equal(a.Key, Assert.Single(await otherStore.AllAsync(Ct)).Key);
        var otherCleanup = new BlockingSignOutCleanup();
        await using var other = CreateSiblingManager(otherStore, otherCleanup);
        Assert.True((await _manager.SignOutAsync(a.Key, Ct)).IsOk);

        Assert.Null(await other.RestoreAsync(Ct));
        Assert.Empty(other.Accounts);
    }

    [Fact]
    public async Task ImplicitLastLogoutPreservesASecondRememberedLoginMissingFromTheLocalRows()
    {
        var a = (await SignInAAsync()).Value!;
        using var otherStore = new AccountStore(_vault);
        var otherCleanup = new BlockingSignOutCleanup { HasStorageLease = false };
        await using var other = CreateSiblingManager(otherStore, otherCleanup);
        Assert.Equal(a.Key, (await other.RestoreAsync(Ct))!.Key);
        var b = (await SignInBAsync()).Value!;
        otherCleanup.HasStorageLease = true;
        otherCleanup.Block = true;

        var signOut = other.SignOutAsync(a.Key, Ct);
        try
        {
            var request = await otherCleanup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            Assert.False(request.All);
            Assert.Equal(a.Key, Assert.Single(request.Keys));
        }
        finally { otherCleanup.Release.TrySetResult(Result<Unit>.Ok(default)); }
        Assert.True((await signOut).IsOk);
        Assert.Equal(b.Key, Assert.Single(_vault.Accounts).Key);

        otherCleanup.Block = false;
        Assert.True((await other.SignOutAllAsync(Ct)).IsOk);
        Assert.Empty(_vault.Accounts);
    }

    private AccountManager CreateSiblingManager(AccountStore store, BlockingSignOutCleanup cleanup)
    {
        var paths = new TestProfilePaths(_root);
        var time = TimeProvider.System;
        var oauthHttp = new OAuthHttp(_http, time);
        var flow = new OAuthFlow(new OAuthDiscovery(oauthHttp), _clients, oauthHttp, new RedirectFollowingLauncher(_http),
            new OAuthFlowOptions { LoopbackPort = 0 }, time);
        return new AccountManager(new JmapClientFactory(_http, time), new MailAccountFactory(_hooks, time), store,
            flow, oauthHttp, paths, ReadOnlyPolicy.AllReadOnly, _hooks, time, signOutCleanup: cleanup);
    }

    [Fact]
    public async Task ALoginHookResumingAfterSignOutCannotReactivateTheDetachedLogin()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hooks.OnNotification = (hook, ct) =>
        {
            if (!string.Equals(hook, HookNames.OnLogin, StringComparison.Ordinal)) return ValueTask.CompletedTask;
            entered.TrySetResult();
            return new ValueTask(release.Task.WaitAsync(ct));
        };
        var login = SignInAAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            var account = Assert.Single(_manager.Accounts);
            Assert.True((await _manager.SignOutAsync(account.Key, Ct)).IsOk);
        }
        finally { release.TrySetResult(); }
        Assert.True((await login).IsOk);
        Assert.Null(_manager.Active);
        Assert.Empty(_manager.Accounts);
        Assert.Empty(_vault.Accounts);
    }

    private sealed class BlockingSignOutCleanup : IAccountSignOutCleanup
    {
        public bool Block { get; set; }
        public bool HasStorageLease { get; set; } = true;
        public TaskCompletionSource<(IReadOnlyList<string> Keys, bool All)> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Result<Unit>> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> TryHoldStorageAuthorityAsync(CancellationToken ct) => Task.FromResult(HasStorageLease);

        public async Task<Result<Unit>> PrepareAsync(IReadOnlyList<string> localAccountKeys, bool allAccounts, CancellationToken ct)
        {
            if (!Block) return Result<Unit>.Ok(default);
            Entered.TrySetResult(([.. localAccountKeys], allAccounts));
            return await Release.Task.WaitAsync(ct);
        }
    }
}
