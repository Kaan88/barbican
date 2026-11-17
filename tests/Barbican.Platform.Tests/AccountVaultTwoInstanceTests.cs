// New tests (fable-review-2 R2-06): two app instances on one profile share the secret store. Each change re-reads the vault and writes or
// removes one account, so neither instance deletes or overwrites what the other wrote from a view it read earlier. Rust oauth.rs:373
// remember, :398 forget_account; ADR 0008 (one entry per account and an index). Owner unified.

using Barbican.Core.Accounts;

namespace Barbican.Platform.Tests;

public sealed class AccountVaultTwoInstanceTests : IDisposable
{
    private static readonly StoredAccount Ana = new(StoredAccount.OAuthKind, "https://jmap.example.org", "ana@example.org") { Refresh = "r1", Endpoint = "https://jmap.example.org/token" };
    private static readonly StoredAccount Ben = new(StoredAccount.PasswordKind, "https://mail.example.net", "ben@example.net") { Password = "pw-b" };
    private static readonly StoredAccount Cy = new(StoredAccount.PasswordKind, "https://mail.example.com", "cy@example.com") { Password = "pw-c" };

    private readonly InMemorySecretStore _shared = new();
    private readonly AccountStore _one;
    private readonly AccountStore _two;

    public AccountVaultTwoInstanceTests()
    {
        _one = new AccountStore(new SecretAccountVault(_shared));
        _two = new AccountStore(new SecretAccountVault(_shared));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _one.Dispose();
        _two.Dispose();
    }

    private async Task<List<StoredAccount>> VaultAsync() => [.. (await new SecretAccountVault(_shared).LoadAsync(Ct)).Value!];

    [Fact]
    public async Task An_account_added_by_one_instance_survives_a_change_by_the_other()
    {
        Assert.True((await _one.RememberAsync(Ana, Ct)).IsOk);
        Assert.Single(await _two.AllAsync(Ct)); // the second instance has read the vault: [ana]

        Assert.True((await _one.RememberAsync(Ben, Ct)).IsOk);

        // a whole-list save from the second instance's view [ana] would have deleted ben's entry
        Assert.True((await _two.RememberAsync(Cy, Ct)).IsOk);
        Assert.Equal([Cy.Key, Ben.Key, Ana.Key], (await VaultAsync()).Select(a => a.Key).ToList());

        // and the cache of the second instance now follows the vault
        Assert.Equal(3, (await _two.AllAsync(Ct)).Count);
    }

    [Fact]
    public async Task A_rotated_token_and_a_sign_out_in_the_other_instance_do_not_undo_each_other()
    {
        await _one.RememberAsync(Ana, Ct);
        await _one.RememberAsync(Ben, Ct);
        _ = await _two.AllAsync(Ct);

        // instance one rotates ana's refresh token; instance two signs ben out from its older view
        await _one.WriteAsync(Ana.Key, "r2", Ct);
        Assert.True((await _two.ForgetAsync(Ben.Key, Ct)).IsOk);

        var left = Assert.Single(await VaultAsync());
        Assert.Equal(Ana.Key, left.Key);
        Assert.Equal("r2", left.Refresh);
        Assert.Equal("r2", await _two.ReadAsync(Ana.Key, Ct));

        // the forgotten account's own entry is gone, not only its name in the index
        Assert.Null((await _shared.ReadAsync(SecretAccountVault.EntryOf(Ben.Key), Ct)).Value);
    }

    [Fact]
    public async Task Default_and_order_change_one_account_at_a_time()
    {
        await _one.RememberAsync(Ana, Ct);
        await _one.RememberAsync(Ben, Ct);
        _ = await _two.AllAsync(Ct);
        await _one.RememberAsync(Cy, Ct);

        Assert.True((await _two.SetDefaultAsync(Ben.Key, Ct)).IsOk);
        Assert.True((await _two.MoveAsync(Ana.Key, 0, Ct)).IsOk);
        var vault = await VaultAsync();
        Assert.Equal([Ana.Key, Cy.Key, Ben.Key], vault.Select(a => a.Key).ToList());
        Assert.Equal(Ben.Key, Assert.Single(vault, a => a.IsDefault).Key);

        // forgetting the default makes the first remaining account the default
        Assert.True((await _one.ForgetAsync(Ben.Key, Ct)).IsOk);
        vault = await VaultAsync();
        Assert.Equal(Ana.Key, Assert.Single(vault, a => a.IsDefault).Key);
    }
}
