// New tests (M2 plan 6.5 exit tests; spec I-013, I-014, I-015, I-031): sign out of one account (its push stream closes, its remembered entry
// goes, the others stay signed in, onBeforeLogout/onAfterLogout/onAccountRemove fire), sign out of all, set as default, reorder. Bulwark
// components/layout/account-switcher.tsx:442-675, stores/account-store.ts:200-240, lib/plugin-hook-catalog.ts:280-283. Owner unified.

using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Tests.Accounts;

public sealed partial class AccountManagerTests
{
    private List<string> HooksFor(string key) =>
        [.. _hooks.Seen.Where(s => s.Payload is AccountHookPayload p && string.Equals(p.AccountId, key, StringComparison.Ordinal)).Select(s => s.Hook)];

    [Fact]
    public async Task Signing_out_of_the_active_account_closes_its_push_forgets_it_and_hands_over_to_the_other()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        Assert.Equal(b.Key, _manager.Active!.Key);

        // the engine of the account on screen runs its push stream (MailEventHub does this in the app)
        using var run = new CancellationTokenSource();
        var engine = _manager.SessionFor(b.Key)!.Sync.RunAsync(run.Token);
        await WaitForAsync(() => _b.Push.OpenCount == 1);
        ActiveAccountChangedEventArgs? changed = null;
        _manager.ActiveChanged += (_, e) => changed = e;

        var r = await _manager.SignOutAsync(b.Key, Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        await WaitForAsync(() => _b.Push.OpenCount == 0);
        await engine.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Null(_manager.SessionFor(b.Key));
        Assert.Equal(a.Key, _manager.Active!.Key);
        Assert.Equal(b.Key, changed!.Previous!.Key);
        Assert.Equal(a.Key, changed.Current!.Key);
        Assert.NotNull(_manager.SessionFor(a.Key));
        Assert.Equal(a.Key, Assert.Single(_vault.Accounts).Key);
        Assert.Equal(a.Key, Assert.Single(_manager.Statuses).Account.Key);

        var hooks = HooksFor(b.Key);
        Assert.Equal([HookNames.OnBeforeLogout, HookNames.OnAfterLogout, HookNames.OnAccountRemove], hooks.Where(h => h is HookNames.OnBeforeLogout or HookNames.OnAfterLogout or HookNames.OnAccountRemove).ToList());
    }

    [Fact]
    public async Task Signing_out_of_an_account_in_the_background_keeps_the_one_on_screen()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        var changes = 0;
        _manager.ActiveChanged += (_, _) => changes++;

        Assert.True((await _manager.SignOutAsync(a.Key, Ct)).IsOk);
        Assert.Equal(b.Key, _manager.Active!.Key);
        Assert.Equal(0, changes);
        Assert.Equal(b.Key, Assert.Single(_vault.Accounts).Key);

        // the remaining account becomes the default (the signed-out one was the first added)
        Assert.True(Assert.Single(_vault.Accounts).IsDefault);
        Assert.True(Assert.Single(_manager.Statuses).IsDefault);
        Assert.False((await _manager.SignOutAsync(a.Key, Ct)).IsOk);
    }

    [Fact]
    public async Task Signing_out_of_all_leaves_no_account_and_no_active_one()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        ActiveAccountChangedEventArgs? changed = null;
        _manager.ActiveChanged += (_, e) => changed = e;

        Assert.True((await _manager.SignOutAllAsync(Ct)).IsOk);
        Assert.Null(_manager.Active);
        Assert.Null(changed!.Current);
        Assert.Empty(_manager.Accounts);
        Assert.Empty(_vault.Accounts);
        Assert.Null(_manager.SessionFor(a.Key));
        foreach (var key in new[] { a.Key, b.Key })
        {
            Assert.Contains(HookNames.OnBeforeLogout, HooksFor(key), StringComparer.Ordinal);
            Assert.Contains(HookNames.OnAccountRemove, HooksFor(key), StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task Set_as_default_and_reorder_are_remembered()
    {
        var a = (await SignInAAsync()).Value!;
        var b = (await SignInBAsync()).Value!;
        Assert.True(_manager.Statuses.Single(s => string.Equals(s.Account.Key, a.Key, StringComparison.Ordinal)).IsDefault);

        Assert.True((await _manager.SetDefaultAsync(b.Key, Ct)).IsOk);
        Assert.Equal(b.Key, Assert.Single(_manager.Statuses, s => s.IsDefault).Account.Key);
        Assert.Equal(b.Key, Assert.Single(_vault.Accounts, s => s.IsDefault).Key);

        Assert.True((await _manager.MoveAsync(a.Key, 0, Ct)).IsOk);
        Assert.Equal([a.Key, b.Key], _manager.Accounts.Select(x => x.Key).ToList());
        Assert.Equal([a.Key, b.Key], _vault.Accounts.Select(x => x.Key).ToList());
        Assert.False((await _manager.SetDefaultAsync("nobody", Ct)).IsOk);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "timed out");
            await Task.Delay(20, Ct);
        }
    }
}
