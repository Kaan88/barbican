// Test wiring of the files add-on methods: the real PluginApiDispatcher over the real file store, writer, transfer service and hook bus on the fake JMAP server (the M5 tree, shared account "s1"), with a spy on the store, the writer and
// the transfer and doubles for the grant and the signed-in session (the things a test flips mid-call). The clock is the rig's fake one, so a minute's budget can be spent and then reset.
#pragma warning disable MA0006, MA0002 // assertions compare string literals

using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Files;
using Barbican.Core.Plugins.Api.Files;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Tests.Files;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginFilesRig : IDisposable
{
    internal const string PluginId = "probe";

    private PluginFilesRig()
    {
    }

    internal FileRig Files { get; private set; } = null!;

    internal SpyFiles Spy { get; private set; } = null!;

    internal SpyFileStore Store { get; private set; } = null!;

    internal PluginCalendarRig.AuthorityDouble Authority { get; private set; } = null!;

    internal PluginCalendarRig.SessionsDouble Sessions { get; private set; } = null!;

    internal PluginApiDispatcher Dispatcher { get; private set; } = null!;

    internal PluginRuntimeBinding Binding { get; private set; } = null!;

    internal static PluginGrantSnapshot Grant(string key, params string[] permissions) =>
        new(new PluginPackageIdentity(PluginId, "1.0.0", new string('a', 64)), 1, true, permissions, [key]);

    internal static async Task<PluginFilesRig> StartAsync(string[]? permissions = null, FakeFiles? files = null, bool capability = true)
    {
        var rig = new PluginFilesRig { Files = await FileRig.CreateAsync(files ?? new FakeFiles { SeedM5 = true, SharedAccounts = ["s1"] }, capability: capability) };
        rig.Spy = new SpyFiles(rig.Files.Writer, rig.Files.Transfer);
        rig.Store = new SpyFileStore(rig.Files.Store);
        var host = new PluginFilesHost(rig.Store, rig.Spy, rig.Spy, new PluginMailRateLimiter(rig.Files.Clock));
        rig.Sessions = new PluginCalendarRig.SessionsDouble(rig.Files.SessionFor(rig.Files.Key)!);
        rig.Authority = new PluginCalendarRig.AuthorityDouble(Grant(rig.Files.Key, permissions ?? ["files:read", "files:write"]));
        rig.Dispatcher = new("auto-files", PluginFilesMethods.Create(host), rig.Authority, rig.Sessions);
        rig.Binding = PluginRuntimeBinding.Create("auto-files", rig.Authority.Current, PluginFrameKind.Background);
        return rig;
    }

    internal async Task<PluginCallOutcome> CallAsync(string method, string args)
    {
        using var doc = JsonDocument.Parse(args);
        return await Dispatcher.DispatchAsync(Binding, new PluginApiRequest("r", method, PluginValue.FromJson(doc.RootElement, new PluginValueLimits(MaxWireBytes: 4 * 1024 * 1024, MaxStringBytes: 2 * 1024 * 1024))), TestContext.Current.CancellationToken);
    }

    internal static List<string> Keys(PluginValue value) => [.. value.PropertyNames.Order(StringComparer.Ordinal)];

    /// <summary>The named properties as text (a number as its digits), in the order given.</summary>
    internal static string[] Pick(PluginValue value, params string[] names) =>
        [.. names.Select(n => value.Property(n).Kind == PluginValueKind.Number ? value.Property(n).NumberText : value.Property(n).StringValue)];

    internal static List<string> Names(PluginValue list) => [.. Enumerable.Range(0, list.Count).Select(i => list.Item(i).Property("name").StringValue)];

    public void Dispose() => Files.Dispose();

    internal sealed class SpyFileStore(IFileStore inner) : IFileStore
    {
        public Action? OnSnapshot { get; set; }

        public event EventHandler<FileChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public IReadOnlyList<string> Favourites => inner.Favourites;

        public IReadOnlyList<string> Recent => inner.Recent;

        public Task<Result<FileSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => inner.LoadAsync(accountKey, ct);

        public bool Supports(string accountKey) => inner.Supports(accountKey);

        public FileSnapshot? Snapshot(string accountKey)
        {
            OnSnapshot?.Invoke();
            return inner.Snapshot(accountKey);
        }

        public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct) => inner.RefreshAsync(accountKey, ct);

        public void ToggleStar(string key) => inner.ToggleStar(key);

        public void AddRecent(string key) => inner.AddRecent(key);

        public void RemoveRecent(string key) => inner.RemoveRecent(key);
    }

    internal sealed class SpyFiles(IFileWriter writer, IFileTransfer transfer) : IFileWriter, IFileTransfer
    {
        public Action? Before { get; set; }

        public int Calls { get; private set; }

        public int Reads { get; private set; }

        public Task<Result<string>> CreateFolderAsync(string accountKey, string? parentKey, string name, CancellationToken ct) => RunAsync(() => writer.CreateFolderAsync(accountKey, parentKey, name, ct));

        public Task<Result<Unit>> RenameAsync(string accountKey, string key, string newName, CancellationToken ct) => RunAsync(() => writer.RenameAsync(accountKey, key, newName, ct));

        public Task<Result<Unit>> MoveAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct) => RunAsync(() => writer.MoveAsync(accountKey, keys, destinationKey, ct));

        public Task<Result<Unit>> CopyAsync(string accountKey, IReadOnlyList<string> keys, string? destinationKey, CancellationToken ct) => RunAsync(() => writer.CopyAsync(accountKey, keys, destinationKey, ct));

        public Task<Result<Unit>> DuplicateAsync(string accountKey, string key, CancellationToken ct) => RunAsync(() => writer.DuplicateAsync(accountKey, key, ct));

        public Task<Result<Unit>> TrashAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => RunAsync(() => writer.TrashAsync(accountKey, keys, ct));

        public Task<Result<Unit>> RestoreAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => RunAsync(() => writer.RestoreAsync(accountKey, keys, ct));

        public Task<Result<Unit>> PurgeAsync(string accountKey, IReadOnlyList<string> keys, CancellationToken ct) => RunAsync(() => writer.PurgeAsync(accountKey, keys, ct));

        public Task<Result<Unit>> EmptyTrashAsync(string accountKey, CancellationToken ct) => RunAsync(() => writer.EmptyTrashAsync(accountKey, ct));

        public Task<Result<Unit>> ShareAsync(string accountKey, string key, string principalId, bool? write, CancellationToken ct) => RunAsync(() => writer.ShareAsync(accountKey, key, principalId, write, ct));

        public Task<Result<IReadOnlyList<string>>> UploadFilesAsync(string accountKey, string? parentKey, IReadOnlyList<string> paths, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct) =>
            RunAsync(() => transfer.UploadFilesAsync(accountKey, parentKey, paths, onConflict, progress, ct));

        public Task<Result<IReadOnlyList<string>>> UploadFolderAsync(string accountKey, string? parentKey, string path, Func<string, Task<FileConflictChoice>> onConflict, IProgress<double>? progress, CancellationToken ct) =>
            RunAsync(() => transfer.UploadFolderAsync(accountKey, parentKey, path, onConflict, progress, ct));

        public Task<Result<string>> DownloadAsync(string accountKey, string key, string directory, CancellationToken ct) => RunAsync(() => transfer.DownloadAsync(accountKey, key, directory, ct));

        public Task<Result<string>> DownloadZipAsync(string accountKey, IReadOnlyList<string> keys, string directory, CancellationToken ct) => RunAsync(() => transfer.DownloadZipAsync(accountKey, keys, directory, ct));

        public Task<Result<string>> CreateTextFileAsync(string accountKey, string? parentKey, string name, string text, CancellationToken ct) => RunAsync(() => transfer.CreateTextFileAsync(accountKey, parentKey, name, text, ct));

        public Task<Result<byte[]>> ReadAsync(string accountKey, string key, int maxBytes, CancellationToken ct)
        {
            Reads++;
            return transfer.ReadAsync(accountKey, key, maxBytes, ct);
        }

        private Task<T> RunAsync<T>(Func<Task<T>> call)
        {
            Calls++;
            Before?.Invoke();
            return call();
        }
    }
}
