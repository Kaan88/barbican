// Test wiring of the files data layer over the fake JMAP server: a login (a FakeJmapServer with FileNode data and a connected client) behind an IAccountManager, a hook bus that records and can refuse, a store, a writer and a transfer. Owner files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Files;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail;
using Barbican.Core.Storage;
using Barbican.FakeJmap;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Files;

internal sealed class FileRig : IAccountManager, IDisposable
{
    private readonly Login _login;

    private FileRig(Login login, FileHooks hooks, string root, JsonObject prefs, FakeTimeProvider clock)
    {
        _login = login;
        Hooks = hooks;
        Root = root;
        Clock = clock;
        Store = new FileStore(this, hooks, new Barbican.Core.Tests.Settings.Prefs.TestProfilePaths(root), clock, watchPush: false);
        Writer = new FileWriter(this, Store, hooks, clock);
        Transfer = new FileTransfer(this, Store, hooks, new Prefs(prefs), clock);
    }

    public FileHooks Hooks { get; }

    public FileStore Store { get; }

    public FileWriter Writer { get; }

    public FileTransfer Transfer { get; }

    public FakeTimeProvider Clock { get; }

    /// <summary>A scratch folder for local files.</summary>
    public string Root { get; }

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public string Key => _login.Info.Key;

    public FakeJmapServer Server => _login.Server;

    public JmapClient Client => _login.Client;

    public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
    {
        add { }
        remove { }
    }

    public IReadOnlyList<AccountInfo> Accounts => [_login.Info];

    public AccountInfo? Active => _login.Info;

    /// <summary>A login whose server has file storage: the M5 tree, shared account "s1", 3 000 bulk files when asked.</summary>
    public static async Task<FileRig> CreateAsync(FakeFiles? files = null, JsonObject? prefs = null, FakeJmapOptions? options = null, bool capability = true)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero));
        var o = (options ?? new FakeJmapOptions()) with { Username = "files@example.org", Origin = "https://files.example.org", FileNodeCapability = capability, Files = capability ? files ?? new FakeFiles { SeedM5 = true, SharedAccounts = ["s1"] } : null, Push = true };
        var server = new FakeJmapServer(o);
        var client = await FakeJmapHarness.ConnectAsync(server, clock, null, false, TestContext.Current.CancellationToken);
        var root = Path.Combine(Path.GetTempPath(), "barbican-files-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(root);
        return new FileRig(new Login(new AccountInfo(AccountKey.Of(o.Origin, o.Username), o.Username, o.Origin), client, server), new FileHooks(), root, prefs ?? [], clock);
    }

    /// <summary>The loaded snapshot of the login.</summary>
    public async Task<FileSnapshot> LoadAsync() => (await Store.LoadAsync(Key, TestContext.Current.CancellationToken)).Value!;

    /// <summary>The entry named <paramref name="name"/> (the first one) in the store, or a failed assertion.</summary>
    public FileEntry Entry(string name, string? parentKey = null) =>
        Store.Snapshot(Key)!.Entries.First(e => string.Equals(e.Name, name, StringComparison.Ordinal) && (parentKey is null || string.Equals(e.ParentKey, parentKey, StringComparison.Ordinal)));

    public IEnumerable<FileEntry> Children(string? parentKey) => Store.Snapshot(Key)!.Entries.Where(e => string.Equals(e.ParentKey, parentKey, StringComparison.Ordinal) && !e.Shared);

    /// <summary>A local file under <see cref="Root"/>.</summary>
    public string Local(string relative, string? text = null)
    {
        var path = Path.Combine(Root, relative);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (text is not null)
        {
            File.WriteAllText(path, text);
        }

        return path;
    }

    /// <summary>Waits until a hook was told (the observers are told in the background).</summary>
    public async Task<object?> HookAsync(string hook)
    {
        for (var i = 0; i < 200; i++)
        {
            if (Hooks.Seen.LastOrDefault(s => string.Equals(s.Hook, hook, StringComparison.Ordinal)) is { Hook: not null } seen)
            {
                return seen.Payload;
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new InvalidOperationException("The hook " + hook + " was not fired.");
    }

    public Task<Result<JmapResponse>> CallAsync(string method, JsonObject args) =>
        Client.CallAsync(JmapRequest.Of(JmapRequest.Call(method, args, "t")), TestContext.Current.CancellationToken);

    public void Dispose()
    {
        Store.Dispose();
        Client.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // a scratch folder
        }
    }

    public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

    public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, Key, StringComparison.Ordinal) ? new Session(_login) : null;

    private sealed record Login(AccountInfo Info, JmapClient Client, FakeJmapServer Server);

    private sealed class Session(Login login) : IAccountSession
    {
        public AccountInfo Account => login.Info;

        public IJmapClient Client => login.Client;

        public IMailService Mail => throw new NotSupportedException();

        public IMailSync Sync => throw new NotSupportedException();
    }

    private sealed class Prefs(JsonObject values) : IPrefsStore
    {
        public PrefsSnapshot Current => PrefsSnapshot.FromJson(values);

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
