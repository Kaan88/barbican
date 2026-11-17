// ADR 0008 (the one-time credential import): the Rust entries are read, never written; odd-length and non-JSON blobs are skipped; both kinds
// of entry (accounts list, single session) are read; the agent path imports password entries only, read-only, and only into dev or auto-*
// profiles; buffers are zeroed. Rust platform/windows.rs (UTF-16 blobs) and main.rs remember_session (the JSON shape).

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Storage;

namespace Barbican.Platform.Tests;

public sealed class RustCredentialImportTests
{
    private const string Password = """{"kind":"password","server":"https://jmap.example.org","user":"ana@example.org","pass":"pw-a"}""";
    private const string OAuth = """{"kind":"oauth","server":"https://jmap.example.org","user":"ben@example.org","refresh":"r-1","endpoint":"https://jmap.example.org/oauth/token","client":"barbican"}""";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProfilePaths Profile(string name) => ProfilePaths.At(name, "c", "d");

    private static async Task<IReadOnlyList<StoredAccount>> AccountsInAsync(InMemorySecretStore store) => (await new SecretAccountVault(store).LoadAsync(Ct)).Value!;

    [Fact]
    public async Task The_agent_imports_password_accounts_read_only_and_skips_oauth()
    {
        var legacy = new FakeLegacySource().Text(RustCredentialImport.AccountsTarget, $"[{Password},{OAuth}]")
            .Text(RustCredentialImport.AdminKeyTarget, "admin-secret");
        var store = new InMemorySecretStore();
        var report = await new RustCredentialImport(legacy, store, Profile("dev")).ImportAsync(ImportMode.Agent, Ct);

        Assert.Equal(1, report.Imported);
        Assert.Contains(report.Skipped, s => s.Contains("(oauth): sign in by hand", StringComparison.Ordinal));
        var a = Assert.Single(await AccountsInAsync(store));
        Assert.Equal("ana@example.org", a.User);
        Assert.True(a.ReadOnly);
        Assert.True(a.IsDefault);

        // the agent never copies the admin or assistant keys, and never reads the admin key entry
        Assert.DoesNotContain(RustCredentialImport.AdminKeyTarget, legacy.Reads, StringComparer.Ordinal);
        Assert.Null((await store.ReadAsync(SecretTarget.AdminKey, Ct)).Value);
        Assert.All(legacy.HandedOut, b => Assert.All(b, x => Assert.Equal(0, x)));
    }

    [Fact]
    public async Task The_user_import_takes_both_kinds_and_the_keys_but_no_refresh_token()
    {
        var legacy = new FakeLegacySource().Text(RustCredentialImport.AccountsTarget, $"[{Password},{OAuth}]")
            .Text(RustCredentialImport.AdminKeyTarget, " admin-secret \n").Text(RustCredentialImport.AiKeyTarget, "ai-secret");
        var store = new InMemorySecretStore();
        var report = await new RustCredentialImport(legacy, store, Profile("main")).ImportAsync(ImportMode.User, Ct);

        Assert.Equal(2, report.Imported);
        var accounts = await AccountsInAsync(store);

        // D-2 (Fable F-06): the Rust app's refresh token stays where it is (a refresh here would rotate it and sign the Rust app out)
        var oauth = Assert.Single(accounts, x => x.IsOAuth);
        Assert.Null(oauth.Refresh);
        Assert.Equal("https://jmap.example.org/oauth/token", oauth.Endpoint);
        Assert.Contains(report.Skipped, s => s.Contains("(oauth): imported without its sign-in", StringComparison.Ordinal));
        foreach (var n in store.Names)
        {
            Assert.DoesNotContain("r-1", (await store.ReadAsync(n, Ct)).Value ?? "", StringComparison.Ordinal);
        }
        Assert.All(accounts, x => Assert.False(x.ReadOnly));
        Assert.Equal("admin-secret", (await store.ReadAsync(SecretTarget.AdminKey, Ct)).Value);
        Assert.Equal("ai-secret", (await store.ReadAsync(SecretTarget.AiKey, Ct)).Value);
    }

    [Fact]
    public async Task The_single_session_entry_is_read_when_there_is_no_list()
    {
        var legacy = new FakeLegacySource().Text(RustCredentialImport.SessionTarget, Password);
        var store = new InMemorySecretStore();
        var report = await new RustCredentialImport(legacy, store, Profile("auto-4242")).ImportAsync(ImportMode.Agent, Ct);
        Assert.Equal(1, report.Imported);
        Assert.Equal("ana@example.org", Assert.Single(await AccountsInAsync(store)).User);
    }

    [Fact]
    public async Task Odd_length_and_non_json_blobs_are_skipped()
    {
        var legacy = new FakeLegacySource().Raw(RustCredentialImport.AccountsTarget, [0x7b, 0x00, 0x22]).Text(RustCredentialImport.SessionTarget, "{not json");
        var store = new InMemorySecretStore();
        var report = await new RustCredentialImport(legacy, store, Profile("dev")).ImportAsync(ImportMode.Agent, Ct);
        Assert.Equal(0, report.Imported);
        Assert.Contains(report.Skipped, s => s.Contains("odd-length", StringComparison.Ordinal));
        Assert.Contains(report.Skipped, s => s.Contains("not JSON", StringComparison.Ordinal));
        Assert.Equal(0, store.Writes);
        Assert.All(legacy.HandedOut, b => Assert.All(b, x => Assert.Equal(0, x)));
    }

    [Fact]
    public void Invalid_utf16_is_skipped()
    {
        var skipped = new List<string>();
        Assert.Null(RustCredentialImport.Decode([0x00, 0xD8, 0x41, 0x00], "x", skipped));
        Assert.Contains(skipped, s => s.Contains("not UTF-16", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Accounts_already_in_the_profile_are_kept_as_they_are()
    {
        var store = new InMemorySecretStore();
        var mine = StoredAccount.FromJson(JsonNode.Parse(Password))! with { Password = "newer" };
        await new SecretAccountVault(store).SaveAsync([mine], Ct);
        var legacy = new FakeLegacySource().Text(RustCredentialImport.AccountsTarget, $"[{Password}]");
        var report = await new RustCredentialImport(legacy, store, Profile("dev")).ImportAsync(ImportMode.Agent, Ct);
        Assert.Equal(0, report.Imported);
        Assert.Equal(1, report.AlreadyPresent);
        Assert.Equal("newer", Assert.Single(await AccountsInAsync(store)).Password);
    }

    [Fact]
    public async Task Each_path_runs_only_in_its_profiles()
    {
        var legacy = new FakeLegacySource().Text(RustCredentialImport.AccountsTarget, $"[{Password}]");
        var store = new InMemorySecretStore();
        Assert.Equal(0, (await new RustCredentialImport(legacy, store, Profile("main")).ImportAsync(ImportMode.Agent, Ct)).Imported);
        Assert.Equal(0, (await new RustCredentialImport(legacy, store, Profile("dev")).ImportAsync(ImportMode.User, Ct)).Imported);
        Assert.Empty(legacy.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void The_vault_keeps_unknown_members_and_skips_junk()
    {
        var withExtra = Password.TrimEnd('}') + ""","futureField":{"x":1}}""";
        var parsed = SecretAccountVault.Parse("[" + withExtra + """, 42, {"kind":"password"}, """ + Password + "]");
        var a = Assert.Single(parsed);
        Assert.Equal(1, a.ToJson()["futureField"]!["x"]!.GetValue<int>());
        Assert.Empty(SecretAccountVault.Parse("not json"));
        Assert.Empty(SecretAccountVault.Parse(null));
    }

    [Fact]
    public async Task The_vault_keeps_one_entry_per_account_and_an_index_without_addresses()
    {
        var store = new InMemorySecretStore();
        var vault = new SecretAccountVault(store);
        var a = StoredAccount.FromJson(JsonNode.Parse(Password))!;
        var b = StoredAccount.FromJson(JsonNode.Parse(OAuth))!;
        await vault.SaveAsync([b, a], Ct);
        Assert.Equal(["ben@example.org", "ana@example.org"], (await vault.LoadAsync(Ct)).Value!.Select(x => x.User), StringComparer.Ordinal);
        Assert.Contains("ben@example.org", (await store.ReadAsync(SecretAccountVault.EntryOf(b.Key), Ct)).Value!, StringComparison.Ordinal);
        var index = (await store.ReadAsync(SecretAccountVault.Index, Ct)).Value!;
        Assert.DoesNotContain("@", index, StringComparison.Ordinal);
        Assert.Equal(3, store.Names.Count);

        // forgetting an account removes its entry
        await vault.SaveAsync([a], Ct);
        Assert.Null((await store.ReadAsync(SecretAccountVault.EntryOf(b.Key), Ct)).Value);
        await vault.SaveAsync([], Ct);
        Assert.Equal([SecretAccountVault.Index], store.Names);
        Assert.Empty((await vault.LoadAsync(Ct)).Value!);
    }

    [Fact]
    public async Task Many_oauth_accounts_fit_where_one_credential_would_not()
    {
        // Fable F-05: Windows caps a generic credential at 2560 bytes; ten OAuth accounts with long refresh tokens are far over that together
        var store = new CappedStore(2560);
        var vault = new SecretAccountVault(store);
        var many = Enumerable.Range(0, 10).Select(i => new StoredAccount(StoredAccount.OAuthKind, "https://jmap.example.org", $"user{i}@example.org")
        {
            Refresh = new string('r', 1500),
            Endpoint = "https://jmap.example.org/oauth/token",
            Client = "barbican",
        }).ToList();
        Assert.True((await vault.SaveAsync(many, Ct)).IsOk);
        Assert.Equal(10, (await vault.LoadAsync(Ct)).Value!.Count);
    }

    [Fact]
    public async Task The_single_entry_layout_is_split_once_and_removed()
    {
        var store = new InMemorySecretStore();
        await store.WriteAsync(SecretTarget.Accounts, $"[{OAuth},{Password}]", Ct);
        await store.WriteAsync(SecretTarget.Session, OAuth, Ct);
        var vault = new SecretAccountVault(store);
        Assert.Equal(["ben@example.org", "ana@example.org"], (await vault.LoadAsync(Ct)).Value!.Select(x => x.User), StringComparer.Ordinal);
        Assert.Null((await store.ReadAsync(SecretTarget.Accounts, Ct)).Value);
        Assert.Null((await store.ReadAsync(SecretTarget.Session, Ct)).Value);
        Assert.Equal(2, (await vault.LoadAsync(Ct)).Value!.Count);
    }

    private sealed class CappedStore(int max) : ISecretStore
    {
        private readonly InMemorySecretStore _inner = new();

        public Task<Barbican.Core.Common.Result<string?>> ReadAsync(SecretName name, CancellationToken ct) => _inner.ReadAsync(name, ct);

        public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> WriteAsync(SecretName name, string value, CancellationToken ct) =>
            System.Text.Encoding.UTF8.GetByteCount(value) > max
                ? Task.FromResult(Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Fail(new Barbican.Core.Jmap.Errors.TransportError("too large", false)))
                : _inner.WriteAsync(name, value, ct);

        public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> DeleteAsync(SecretName name, CancellationToken ct) => _inner.DeleteAsync(name, ct);
    }

    [Fact]
    public void Secret_targets_are_namespaced_by_profile()
    {
        Assert.Equal("Barbican.Next/dev/accounts", SecretTarget.Format("dev", SecretTarget.Accounts));
        Assert.Equal("Barbican.Next/auto-1/refresh/ana", SecretTarget.Format("auto-1", new SecretName("refresh", "ana")));
        Assert.Throws<ArgumentException>(() => SecretTarget.Format("a/b", SecretTarget.Accounts));
        Assert.Throws<ArgumentException>(() => SecretTarget.Format("dev", new SecretName("")));
    }
}
