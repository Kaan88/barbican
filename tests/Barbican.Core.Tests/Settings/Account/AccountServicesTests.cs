// Account settings services against FakeJmap; no live security writes.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Settings.Account;
using Barbican.Core.Tests.Contacts;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Settings.Account;

public sealed class AccountServicesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Identity_create_update_order_and_delete_round_trip()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", null, new FakeJmapOptions { MetadataCapability = true });
        var identities = new AccountIdentities(rig, rig.Hooks);
        var before = await identities.ListAsync(key, Ct);
        Assert.True(before.IsOk, before.Error?.Message);
        var created = await identities.CreateAsync(key, new AccountIdentityEdit("Alias", "alias@example.org", null, null, "signature", null), Ct);
        Assert.True(created.IsOk, created.Error?.Message);
        var id = created.Value!.Id;
        var updated = await identities.UpdateAsync(key, id, new AccountIdentityEdit("Updated", null, null, null, "changed", null), Ct);
        Assert.True(updated.IsOk, updated.Error?.Message);
        var ordered = await identities.SetOrderAsync(key, [id], Ct);
        Assert.True(ordered.IsOk, ordered.Error?.Message);
        var list = await identities.ListAsync(key, Ct);
        Assert.True(list.IsOk, list.Error?.Message);
        Assert.Equal(id, list.Value![0].Id);
        Assert.Equal("Updated", list.Value[0].Name);
        var deleted = await identities.DeleteAsync(key, id, Ct);
        Assert.True(deleted.IsOk, deleted.Error?.Message);
        Assert.DoesNotContain((await identities.ListAsync(key, Ct)).Value!, i => string.Equals(i.Id, id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_intercept_hook_stops_identity_changes_with_a_typed_error()
    {
        var rig = ContactRig.Create();
        var (key, _, _) = await rig.AddAsync("user@example.org", null, new FakeJmapOptions { MetadataCapability = true });
        var identities = new AccountIdentities(rig, rig.Hooks);
        var created = await identities.CreateAsync(key, new AccountIdentityEdit("Alias", "alias@example.org", null, null, "signature", null), Ct);
        Assert.True(created.IsOk, created.Error?.Message);
        rig.Hooks.Stop(HookNames.OnBeforeIdentityChange);
        AssertStopped(await identities.CreateAsync(key, new AccountIdentityEdit("Second", "second@example.org", null, null, null, null), Ct));
        AssertStopped(await identities.UpdateAsync(key, created.Value!.Id, new AccountIdentityEdit("Updated", null, null, null, "changed", null), Ct));
        AssertStopped(await identities.DeleteAsync(key, created.Value.Id, Ct));
        Assert.Single((await identities.ListAsync(key, Ct)).Value!, i => string.Equals(i.Id, created.Value.Id, StringComparison.Ordinal));
    }

    private static void AssertStopped<T>(Result<T> result)
    {
        var error = Assert.IsType<HookStoppedError>(result.Error);
        Assert.Equal("A plug-in stopped this change.", error.Message);
    }

    [Fact]
    public async Task Security_refuses_wrong_password_and_issues_secret_only_once()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", null, new FakeJmapOptions { AccountSecurity = new FakeAccountSecurity() });
        var security = new AccountSecurity(rig, rig.Hooks);
        Assert.True(security.IsAvailable(key));
        Assert.False((await security.ChangePasswordAsync(key, "wrong-password", "new-fake-password", Ct)).IsOk);
        Assert.True((await security.ChangePasswordAsync(key, "fake-current-password", "new-fake-password", Ct)).IsOk);
        var issued = await security.CreateAppPasswordAsync(key, "Test application", null, Ct);
        Assert.True(issued.IsOk, issued.Error?.Message);
        Assert.NotEmpty(issued.Value!.Secret);
        var listed = await security.AppPasswordsAsync(key, Ct);
        Assert.True(listed.IsOk, listed.Error?.Message);
        Assert.Contains(listed.Value!, x => string.Equals(x.Id, issued.Value.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(server.Api.SecuritySetRequests, entry => entry.Args.ToJsonString().Contains("new-fake-password", StringComparison.Ordinal));
        Assert.True((await security.DeleteAppPasswordAsync(key, issued.Value.Id, Ct)).IsOk);
        Assert.Empty((await security.AppPasswordsAsync(key, Ct)).Value!);
    }
    [Fact]
    public async Task Totp_enrollment_and_api_keys_are_fake_only_and_request_log_redacts_secrets()
    {
        var rig = ContactRig.Create();
        var (key, server, _) = await rig.AddAsync("user@example.org", null, new FakeJmapOptions { AccountSecurity = new FakeAccountSecurity() });
        var security = new AccountSecurity(rig, rig.Hooks);
        var started = await security.BeginTotpAsync(key, "fake-current-password", Ct);
        Assert.True(started.IsOk, started.Error?.Message);
        Assert.True((await security.ConfirmTotpAsync(key, "123456", Ct)).IsOk);
        Assert.True((await security.TotpAsync(key, Ct)).Value!.Enabled);
        Assert.DoesNotContain(server.Api.SecuritySetRequests, entry => entry.Args.ToJsonString().Contains("otpauth://", StringComparison.Ordinal));
        Assert.True((await security.DisableTotpAsync(key, "fake-current-password", Ct)).IsOk);
        Assert.False((await security.TotpAsync(key, Ct)).Value!.Enabled);
        var api = await security.CreateApiKeyAsync(key, "Test API", null, Ct);
        Assert.True(api.IsOk, api.Error?.Message);
        Assert.Contains((await security.ApiKeysAsync(key, Ct)).Value!, x => string.Equals(x.Id, api.Value!.Id, StringComparison.Ordinal));
        Assert.True((await security.DeleteApiKeyAsync(key, api.Value!.Id, Ct)).IsOk);
    }

}
