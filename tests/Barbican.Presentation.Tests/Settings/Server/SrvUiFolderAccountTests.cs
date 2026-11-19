using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Settings.Folders;
using Barbican.Presentation.Common;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Server;
using Barbican.Presentation.Tests.Accounts;

namespace Barbican.Presentation.Tests.Settings.Server;

public sealed class SrvUiFolderAccountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Colliding_folder_ids_never_use_the_previous_accounts_selection()
    {
        var (accounts, a, b) = Accounts();
        var source = new ControlledFolders();
        var admin = new RecordingAdmin();
        var first = source.Queue(a.Key);
        var vm = new SrvUiFoldersViewModel(admin, source, accounts, new InlineDispatcher());
        var load = vm.LoadAsync(Ct);
        first.SetResult(Ok("A folder")); await load;
        vm.Select("same-id");
        var second = source.Queue(b.Key);
        await accounts.SwitchToAsync(b.Key, Ct);
        Assert.False(await vm.RenameAsync("Wrong", Ct));
        Assert.Equal(0, admin.Writes);
        second.SetResult(Ok("B folder"));
        Assert.True(SpinWait.SpinUntil(() => vm.Rows.Any(x => string.Equals(x.Node.Name, "B folder", StringComparison.Ordinal)), TimeSpan.FromSeconds(5)));
        Assert.Null(vm.SelectedId);
        Assert.Equal("B folder", Assert.Single(vm.Rows).Node.Name);
    }

    [Fact]
    public async Task Late_folder_snapshots_are_discarded_after_a_b_a()
    {
        var (accounts, a, b) = Accounts();
        var source = new ControlledFolders();
        var admin = new RecordingAdmin();
        var first = source.Queue(a.Key);
        var vm = new SrvUiFoldersViewModel(admin, source, accounts, new InlineDispatcher());
        var load = vm.LoadAsync(Ct); first.SetResult(Ok("Original A")); await load;
        var bLoad = source.Queue(b.Key); await accounts.SwitchToAsync(b.Key, Ct);
        Assert.True(SpinWait.SpinUntil(() => source.Calls >= 2, TimeSpan.FromSeconds(5)));
        var aLoad = source.Queue(a.Key); await accounts.SwitchToAsync(a.Key, Ct);
        Assert.True(SpinWait.SpinUntil(() => source.Calls >= 3, TimeSpan.FromSeconds(5)));
        aLoad.SetResult(Ok("Fresh A"));
        Assert.True(SpinWait.SpinUntil(() => vm.Rows.Any(x => string.Equals(x.Node.Name, "Fresh A", StringComparison.Ordinal)), TimeSpan.FromSeconds(5)));
        bLoad.SetResult(Ok("Late B"));
        Assert.Equal("Fresh A", Assert.Single(vm.Rows).Node.Name);
    }

    private static Result<IReadOnlyList<SrvUiFolderSnapshot>> Ok(string name) => Result<IReadOnlyList<SrvUiFolderSnapshot>>.Ok([new(new FolderAdminNode("same-id", null, name, null), 0, true)]);
    private static (FakeAccounts Accounts, AccountInfo A, AccountInfo B) Accounts()
    {
        var a = FakeAccounts.Info("a@example.org"); var b = FakeAccounts.Info("b@example.org"); var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(a, true, true, true, null, null), new AccountStatus(b, false, false, true, null, null)); return (accounts, a, b);
    }

    private sealed class ControlledFolders : ISrvUiFolderSource
    {
        private readonly Dictionary<string, Queue<TaskCompletionSource<Result<IReadOnlyList<SrvUiFolderSnapshot>>>>> _pending = new(StringComparer.Ordinal);
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public event EventHandler? Changed { add { } remove { } }
        public TaskCompletionSource<Result<IReadOnlyList<SrvUiFolderSnapshot>>> Queue(string key)
        {
            var gate = new TaskCompletionSource<Result<IReadOnlyList<SrvUiFolderSnapshot>>>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pending) { if (!_pending.TryGetValue(key, out var q)) _pending[key] = q = new(); q.Enqueue(gate); } return gate;
        }
        public Task<Result<IReadOnlyList<SrvUiFolderSnapshot>>> LoadAsync(string key, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls); lock (_pending) return _pending[key].Dequeue().Task;
        }
        public IReadOnlyList<FolderAdminNode> Nodes() => [new("same-id", null, "Untrusted sidebar", null)];
        public int Unread(string id) => 0;
        public bool Subscribed(string id) => true;
    }

    private sealed class RecordingAdmin : IFolderAdmin
    {
        private readonly SampleFolderAdmin _inner = new();
        public int Writes { get; private set; }
        public event EventHandler<FolderAdminChangedEventArgs>? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public Task<Result<FolderAdminPrefs>> PrefsAsync(string a, CancellationToken ct) => _inner.PrefsAsync(a, ct);
        public Task<Result<string>> CreateAsync(string a, string? p, string n, CancellationToken ct) { Writes++; return _inner.CreateAsync(a, p, n, ct); }
        public Task<Result<Unit>> RenameAsync(string a, string id, string n, CancellationToken ct) { Writes++; return _inner.RenameAsync(a, id, n, ct); }
        public Task<Result<Unit>> MoveAsync(string a, IReadOnlyList<string> ids, string? p, CancellationToken ct) { Writes++; return _inner.MoveAsync(a, ids, p, ct); }
        public Task<Result<Unit>> DeleteAsync(string a, string id, CancellationToken ct) { Writes++; return _inner.DeleteAsync(a, id, ct); }
        public Task<Result<Unit>> SubscribeAsync(string a, string id, bool sub, CancellationToken ct) { Writes++; return _inner.SubscribeAsync(a, id, sub, ct); }
        public Task<Result<Unit>> SetRoleAsync(string a, string id, string? r, CancellationToken ct) { Writes++; return _inner.SetRoleAsync(a, id, r, ct); }
        public Task<Result<Unit>> MarkTreeReadAsync(string a, string id, CancellationToken ct) { Writes++; return _inner.MarkTreeReadAsync(a, id, ct); }
        public Task<Result<Unit>> SetPrefAsync(string a, string id, FolderAdminPref p, string? v, CancellationToken ct) { Writes++; return _inner.SetPrefAsync(a, id, p, v, ct); }
    }
}
