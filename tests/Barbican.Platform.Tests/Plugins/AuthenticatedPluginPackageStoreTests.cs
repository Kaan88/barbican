// Host-added authenticated package persistence; uses only temporary profiles and in-memory secrets.
using System.Security.Cryptography;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Persistence;
using Barbican.Core.Storage;
using Barbican.Platform.Plugins;
namespace Barbican.Platform.Tests.Plugins;

public sealed class AuthenticatedPluginPackageStoreTests
{
    [Fact]
    public async Task AtomicCommitPersistsAndReopensWithAuthenticatedReadback()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            var initial = Need(await store.LoadAsync(paths.Profile, Ct));
            Assert.Equal(0, initial.Revision);
            Assert.Equal(1, Need(await store.CommitAsync(paths.Profile, 0, new byte[] { 10, 20 }, Ct)));
            using var reopened = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            var persisted = Need(await reopened.LoadAsync(paths.Profile, Ct));
            Assert.Equal(1, persisted.Revision);
            Assert.Equal(new byte[] { 10, 20 }, persisted.Index.ToArray());
            Assert.False((await reopened.CommitAsync(paths.Profile, 0, new byte[] { 11 }, Ct)).IsOk);
        });
    }

    [Fact]
    public async Task TamperingAndRollbackNeverRestoreAnOldAuthenticatedGrant()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            var path = Path.Combine(paths.ConfigDir, "plugins", "packages", "index.dat");
            var old = await File.ReadAllBytesAsync(path, Ct);
            Assert.True((await store.CommitAsync(paths.Profile, 0, new byte[] { 42 }, Ct)).IsOk);
            await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(path)!, "index.alternate.dat"), old, Ct);
            Assert.False((await store.LoadAsync(paths.Profile, Ct)).IsOk);
            Assert.False((await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct)).IsOk);
        });
    }

    [Fact]
    public async Task AnUnreadableInactiveIndexDoesNotHideTheAuthenticatedIndex()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.Equal(1, Need(await store.CommitAsync(paths.Profile, 0, new byte[] { 42 }, Ct)));

            var inactive = Path.Combine(paths.ConfigDir, "plugins", "packages", "index.dat");
            await using (var file = File.OpenWrite(inactive))
            {
                file.SetLength((4 * 1024 * 1024) + 41);
            }

            using var reopened = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.Equal(new byte[] { 42 }, Need(await reopened.LoadAsync(paths.Profile, Ct)).Index.ToArray());
        });
    }

    [Fact]
    public async Task ArchiveContentIsHashBoundAndLeaseLossFailsClosed()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            byte[] bytes = [2, 3, 5];
            var identity = new PluginPackageIdentity("synthetic", "1.0.0", Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.True((await store.PutContentAsync(paths.Profile, identity, bytes, Ct)).IsOk);
            Assert.Equal(bytes, Need(await store.ReadContentAsync(paths.Profile, identity, Ct)).ToArray());
            Assert.False((await store.PutContentAsync(paths.Profile, identity, new byte[] { 2, 3 }, Ct)).IsOk);
            await File.WriteAllBytesAsync(Path.Combine(paths.ConfigDir, "plugins", "packages", identity.Sha256 + ".zip"), new byte[] { 9 }, Ct);
            Assert.False((await store.ReadContentAsync(paths.Profile, identity, Ct)).IsOk);
            await lease.DisposeAsync();
            Assert.False((await store.LoadAsync(paths.Profile, Ct)).IsOk);
            Assert.False((await store.ReadContentAsync(paths.Profile, identity, Ct)).IsOk);
        });
    }

    [Fact]
    public async Task LeaseCannotAuthorizeSameProfileNameAtAnotherRoot()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            var other = Path.Combine(paths.ConfigDir, "other");
            Directory.CreateDirectory(other);
            var unrelated = ProfilePaths.At(paths.Profile, other, other);
            Assert.False((await AuthenticatedPluginPackageStore.OpenAsync(unrelated, lease, secrets, Ct)).IsOk);
        });
    }

    [Fact]
    public async Task MissingSecretOrAlteredIndexNeverAuthenticates()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.False((await store.LoadAsync("agent-other", Ct)).IsOk);
            var path = Path.Combine(paths.ConfigDir, "plugins", "packages", "index.dat");
            var original = await File.ReadAllBytesAsync(path, Ct);
            var altered = original.ToArray();
            altered[^1] ^= 1;
            await File.WriteAllBytesAsync(path, altered, Ct);
            Assert.False((await store.LoadAsync(paths.Profile, Ct)).IsOk);
            await File.WriteAllBytesAsync(path, original, Ct);
            await secrets.DeleteAsync(new SecretName("plugin-authority-v1", paths.Profile), Ct);
            Assert.False((await store.LoadAsync(paths.Profile, Ct)).IsOk);
            Assert.False((await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct)).IsOk);
        });
    }

    [Fact]
    public async Task FailureUpdatingSecretPointerPreservesTheCommittedIndexAcrossRestart()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.Equal(1, Need(await store.CommitAsync(paths.Profile, 0, new byte[] { 7 }, Ct)));
            secrets.FailWrites = true;
            Assert.False((await store.CommitAsync(paths.Profile, 1, new byte[] { 8 }, Ct)).IsOk);
            Assert.Equal(new byte[] { 7 }, Need(await store.LoadAsync(paths.Profile, Ct)).Index.ToArray());
            secrets.FailWrites = false;

            using var reopened = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.Equal(1, Need(await reopened.LoadAsync(paths.Profile, Ct)).Revision);
            Assert.Equal(2, Need(await reopened.CommitAsync(paths.Profile, 1, new byte[] { 9 }, Ct)));
            Assert.Equal(new byte[] { 9 }, Need(await reopened.LoadAsync(paths.Profile, Ct)).Index.ToArray());
        });
    }

    [Fact]
    public async Task FirstOpenCanRetryAfterTheSecretWriteFails()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            secrets.FailWrites = true;
            Assert.False((await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct)).IsOk);
            secrets.FailWrites = false;
            using var reopened = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.Equal(0, Need(await reopened.LoadAsync(paths.Profile, Ct)).Revision);
        });
    }

    [Fact]
    public async Task LostSecretAcknowledgementCanBeResolvedOnlyByExactAuthenticatedReadback()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            secrets.FailAfterWrite = true;
            Assert.False((await store.CommitAsync(paths.Profile, 0, new byte[] { 7 }, Ct)).IsOk);
            secrets.FailAfterWrite = false;
            var recovered = Need(await store.LoadAsync(paths.Profile, Ct));
            Assert.Equal(1, recovered.Revision);
            Assert.Equal(new byte[] { 7 }, recovered.Index.ToArray());
            using var reopened = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            Assert.Equal(1, Need(await reopened.LoadAsync(paths.Profile, Ct)).Revision);
        });
    }

    [Fact]
    public async Task CommitCopiesCallerBytesBeforeWaitingForSecretStore()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            secrets.BlockNextRead = true;
            var bytes = new byte[] { 1, 2 };
            var pending = store.CommitAsync(paths.Profile, 0, bytes, Ct);
            await secrets.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
            bytes[0] = 99;
            secrets.ReleaseRead.TrySetResult();
            Assert.True((await pending).IsOk);
            Assert.Equal(new byte[] { 1, 2 }, Need(await store.LoadAsync(paths.Profile, Ct)).Index.ToArray());
        });
    }

    private sealed class TestSecrets : ISecretStore
    {
        private readonly InMemorySecretStore _inner = new();
        public bool FailWrites { get; set; }
        public bool FailAfterWrite { get; set; }
        public bool BlockNextRead { get; set; }
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Barbican.Core.Common.Result<string?>> ReadAsync(SecretName name, CancellationToken ct)
        {
            if (BlockNextRead)
            {
                BlockNextRead = false;
                ReadEntered.TrySetResult();
                await ReleaseRead.Task.WaitAsync(ct);
            }
            return await _inner.ReadAsync(name, ct);
        }
        public async Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> WriteAsync(SecretName name, string value, CancellationToken ct)
        {
            if (FailWrites) return Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("Synthetic secret write failure."));
            var result = await _inner.WriteAsync(name, value, ct);
            return FailAfterWrite ? Barbican.Core.Common.Result<Barbican.Core.Common.Unit>.Fail(new Barbican.Core.Jmap.Errors.ValidationError("Synthetic lost acknowledgement.")) : result;
        }
        public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> DeleteAsync(SecretName name, CancellationToken ct) => _inner.DeleteAsync(name, ct);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static T Need<T>(Barbican.Core.Common.Result<T> result) { Assert.True(result.IsOk, result.Error?.Message); return result.Value!; }
    private static async Task WithProfileAsync(Func<IProfilePaths, IPluginProfileAuthorityLease, TestSecrets, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = ProfilePaths.At("agent-store", root, root);
            var lease = Need(await new PluginProfileAuthorityLeaseFactory().AcquireAsync(paths, Ct));
            try { await action(paths, lease, new TestSecrets()); }
            finally { await lease.DisposeAsync(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
