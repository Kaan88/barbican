using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform.Tests;

public sealed class SecretAssistantKeysTests
{
    [Fact]
    public async Task Client_reads_the_settings_pages_entry_and_removal_clears_it()
    {
        var store = new InMemorySecretStore();
        var name = new SecretName("ai-api-key");
        _ = await store.WriteAsync(name, "fixture-key", CancellationToken.None);
        var keys = new SecretAssistantKeys(store);
        Assert.Equal("fixture-key", await keys.ReadAsync(CancellationToken.None));
        await keys.WriteAsync("replacement-key", CancellationToken.None);
        Assert.Equal("replacement-key", (await store.ReadAsync(name, CancellationToken.None)).Value);
        await keys.WriteAsync(null, CancellationToken.None);
        Assert.Null(await keys.ReadAsync(CancellationToken.None));
        Assert.Empty(store.Names);
    }

    [Fact]
    public async Task A_failed_write_is_not_reported_as_success_or_exposes_store_details()
    {
        var keys = new SecretAssistantKeys(new FailedStore());
        Assert.Null(await keys.ReadAsync(CancellationToken.None));
        var error = await Assert.ThrowsAsync<IOException>(() => keys.WriteAsync("fixture-key", CancellationToken.None));
        Assert.DoesNotContain("private-store-detail", error.Message, StringComparison.Ordinal);
    }

    private sealed class FailedStore : ISecretStore
    {
        public Task<Result<string?>> ReadAsync(SecretName name, CancellationToken ct) => Task.FromResult(Result<string?>.Fail(new ValidationError("private-store-detail")));
        public Task<Result<Unit>> WriteAsync(SecretName name, string value, CancellationToken ct) => Task.FromResult(Result<Unit>.Fail(new ValidationError("private-store-detail")));
        public Task<Result<Unit>> DeleteAsync(SecretName name, CancellationToken ct) => Task.FromResult(Result<Unit>.Fail(new ValidationError("private-store-detail")));
    }
}
