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
    [Red("m6b-package-store")]
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
    [Red("m6b-package-store")]
    public async Task TamperingAndRollbackNeverRestoreAnOldAuthenticatedGrant()
    {
        await WithProfileAsync(async (paths, lease, secrets) =>
        {
            using var store = Need(await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct));
            var path = Path.Combine(paths.ConfigDir, "plugins", "packages", "index.dat");
            var old = await File.ReadAllBytesAsync(path, Ct);
            Assert.True((await store.CommitAsync(paths.Profile, 0, new byte[] { 42 }, Ct)).IsOk);
            await File.WriteAllBytesAsync(path, old, Ct);
            Assert.False((await store.LoadAsync(paths.Profile, Ct)).IsOk);
            Assert.False((await AuthenticatedPluginPackageStore.OpenAsync(paths, lease, secrets, Ct)).IsOk);
        });
    }

    [Fact]
    [Red("m6b-package-store")]
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
            await lease.DisposeAsync();
            Assert.False((await store.LoadAsync(paths.Profile, Ct)).IsOk);
            Assert.False((await store.ReadContentAsync(paths.Profile, identity, Ct)).IsOk);
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static T Need<T>(Barbican.Core.Common.Result<T> result) { Assert.True(result.IsOk, result.Error?.Message); return result.Value!; }
    private static async Task WithProfileAsync(Func<IProfilePaths, IPluginProfileAuthorityLease, InMemorySecretStore, Task> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "barbican-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = ProfilePaths.At("agent-store", root, root);
            var lease = Need(await new PluginProfileAuthorityLeaseFactory().AcquireAsync(paths, Ct));
            try { await action(paths, lease, new InMemorySecretStore()); }
            finally { await lease.DisposeAsync(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
