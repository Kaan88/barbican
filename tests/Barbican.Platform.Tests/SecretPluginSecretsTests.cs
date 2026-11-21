using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform.Tests;

public sealed class SecretPluginSecretsTests
{
    [Fact]
    public async Task The_assistant_keeps_its_pre_add_on_key_entry_and_removal_clears_it()
    {
        var store = new InMemorySecretStore();
        var name = new SecretName("ai-api-key");
        _ = await store.WriteAsync(name, "fixture-key", CancellationToken.None);
        var keys = new SecretPluginSecrets(store);
        Assert.Equal("fixture-key", await keys.ReadAsync("assistant", "api-key", CancellationToken.None));
        await keys.WriteAsync("assistant", "api-key", "replacement-key", CancellationToken.None);
        Assert.Equal("replacement-key", (await store.ReadAsync(name, CancellationToken.None)).Value);
        await keys.WriteAsync("assistant", "api-key", null, CancellationToken.None);
        Assert.Null(await keys.ReadAsync("assistant", "api-key", CancellationToken.None));
        Assert.Empty(store.Names);
    }

    [Fact]
    public async Task Other_add_ons_get_their_own_scoped_entries()
    {
        var store = new InMemorySecretStore();
        var keys = new SecretPluginSecrets(store);
        await keys.WriteAsync("other", "api-key", "other-key", CancellationToken.None);
        Assert.Null(await keys.ReadAsync("assistant", "api-key", CancellationToken.None));
        Assert.Equal("other-key", await keys.ReadAsync("other", "api-key", CancellationToken.None));
        Assert.DoesNotContain(new SecretName("ai-api-key"), store.Names);
    }

    [Fact]
    public async Task A_failed_write_is_not_reported_as_success_or_exposes_store_details()
    {
        var keys = new SecretPluginSecrets(new FailedStore());
        Assert.Null(await keys.ReadAsync("assistant", "api-key", CancellationToken.None));
        var error = await Assert.ThrowsAsync<IOException>(() => keys.WriteAsync("assistant", "api-key", "fixture-key", CancellationToken.None));
        Assert.DoesNotContain("private-store-detail", error.Message, StringComparison.Ordinal);
    }

    private sealed class FailedStore : ISecretStore
    {
        public Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct) => Task.FromResult(Result<string?>.Fail(new ValidationError("private-store-detail")));
        public Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct) => Task.FromResult(Result<Unit>.Fail(new ValidationError("private-store-detail")));
        public Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct) => Task.FromResult(Result<Unit>.Fail(new ValidationError("private-store-detail")));
    }
}
