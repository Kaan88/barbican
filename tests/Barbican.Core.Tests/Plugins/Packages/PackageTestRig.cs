// M6b package lifecycle acceptance fixtures: all persistent state here is in-memory and synthetic.
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Packages;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;
using Barbican.Core.Plugins.Persistence;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Packages;

internal sealed class PackageTestRig
{
    public const string Profile = "agent-package";
    public const string PluginId = "fixture-plugin";
    public readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    public readonly Store Persistence = new();
    public readonly UserStore Data = new();
    public readonly Revocation Cleanup = new();
    public readonly Sessions Accounts = new();
    public PluginPackageManager Manager() => new(new PluginPackageManagerOptions(Profile, "1.21.2"), Persistence, Cleanup, Time);
    public PluginPackageManager LeasedManager(string profile = Profile, IPluginRevocationCoordinator? revocation = null) =>
        new(new PluginPackageManagerOptions(profile, "1.21.2"), Persistence, revocation ?? Cleanup, Time, new TestLease(profile));
    public PluginScopedStorage Storage(PluginPackageManager manager, string profile = Profile) => new(profile, manager, Accounts, Data);

    private sealed class TestLease(string profile) : IPluginProfileAuthorityLease
    {
        public string Profile { get; } = profile;
        public string ConfigRoot => string.Empty;
        public bool IsHeld => true;
        public CancellationToken Lifetime => CancellationToken.None;
        public bool TryRunWhileHeld(Func<bool> write) => write();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public static MemoryStream Archive(string source = "export default 1", params string[] permissions) => ArchiveForId(PluginId, source, permissions);

    public static MemoryStream ArchiveForId(string pluginId, string source, params string[] permissions)
    {
        var manifest = new JsonObject
        {
            ["id"] = pluginId, ["name"] = "Fixture", ["version"] = "1.0.0", ["author"] = "Example",
            ["type"] = "hook", ["permissions"] = new JsonArray([.. permissions.Select(x => (JsonNode?)JsonValue.Create(x))]),
        };
        return Zip(("manifest.json", Encoding.UTF8.GetBytes(manifest.ToJsonString())), ("index.js", Encoding.UTF8.GetBytes(source)));
    }

    public static MemoryStream Zip(params (string Path, byte[] Bytes)[] entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in entries)
            {
                var item = zip.CreateEntry(path);
                item.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = item.Open();
                stream.Write(bytes);
            }
        }
        buffer.Position = 0;
        return buffer;
    }

    public static PluginValue StringValue(string value)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return PluginValue.FromJson(json.RootElement);
    }

    internal sealed class Store : IPluginPackageStore
    {
        private readonly Dictionary<string, PluginStoreSnapshot> _indices = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Profile, string Hash), ImmutableArray<byte>> _content = [];
        public bool FailBeforeCommit { get; set; }
        public long? FailCommitAtRevision { get; set; }
        public bool ThrowOnCommit { get; set; }
        public bool FailAfterCommit { get; set; }
        public bool BadAuthentication { get; set; }
        public bool FailDelete { get; set; }
        public int Deletes { get; private set; }
        public int Loads { get; private set; }
        public TaskCompletionSource? LoadEntered { get; set; }
        public TaskCompletionSource? ReleaseLoad { get; set; }
        public TaskCompletionSource? PublishEntered { get; set; }
        public TaskCompletionSource? ReleasePublish { get; set; }
        public async Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, CancellationToken ct)
        {
            Loads++;
            LoadEntered?.TrySetResult();
            if (ReleaseLoad is not null) await ReleaseLoad.Task.WaitAsync(ct);
            return BadAuthentication
                ? Result<PluginStoreSnapshot>.Fail(new ValidationError("Synthetic authentication failure."))
                : Result<PluginStoreSnapshot>.Ok(_indices.GetValueOrDefault(profile) ?? new PluginStoreSnapshot(0, []));
        }
        public Task<Result<long>> CommitAsync(string profile, long expectedRevision, ReadOnlyMemory<byte> index, CancellationToken ct)
            => Task.FromResult(Commit(profile, expectedRevision, index));

        public async Task<Result<long>> CommitAsync(string profile, long expectedRevision, ReadOnlyMemory<byte> index,
            Func<Func<bool>, bool> publishWhileCurrent, CancellationToken ct)
        {
            var copy = index.ToArray();
            PublishEntered?.TrySetResult();
            if (ReleasePublish is not null) await ReleasePublish.Task.WaitAsync(ct);
            var result = Result<long>.Fail(new ValidationError("Synthetic publication was refused."));
            _ = publishWhileCurrent(() =>
            {
                if (ct.IsCancellationRequested) return false;
                result = Commit(profile, expectedRevision, copy);
                return true;
            });
            return result;
        }

        private Result<long> Commit(string profile, long expectedRevision, ReadOnlyMemory<byte> index)
        {
            if (ThrowOnCommit) throw new IOException("Synthetic storage failure.");
            var before = _indices.GetValueOrDefault(profile) ?? new PluginStoreSnapshot(0, []);
            if (FailBeforeCommit || FailCommitAtRevision == expectedRevision || before.Revision != expectedRevision) return Result<long>.Fail(new ValidationError("Synthetic commit failure."));
            var next = new PluginStoreSnapshot(before.Revision + 1, ImmutableArray.CreateRange(index.ToArray()));
            _indices[profile] = next;
            return FailAfterCommit ? Result<long>.Fail(new ValidationError("Synthetic uncertain acknowledgement.")) : Result<long>.Ok(next.Revision);
        }
        public Task<Result<Unit>> PutContentAsync(string profile, PluginPackageIdentity identity, ReadOnlyMemory<byte> archive, CancellationToken ct)
        {
            _content[(profile, identity.Sha256)] = ImmutableArray.CreateRange(archive.ToArray());
            return Task.FromResult(Result<Unit>.Ok(default));
        }
        public Task<Result<ImmutableArray<byte>>> ReadContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct) =>
            Task.FromResult(_content.TryGetValue((profile, identity.Sha256), out var bytes) ? Result<ImmutableArray<byte>>.Ok(bytes) : Result<ImmutableArray<byte>>.Fail(new ValidationError("Missing synthetic content.")));
        public Task<Result<Unit>> DeleteContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct)
        {
            Deletes++;
            if (FailDelete) return Task.FromResult(Result<Unit>.Fail(new ValidationError("Synthetic deletion failure.")));
            _content.Remove((profile, identity.Sha256));
            return Task.FromResult(Result<Unit>.Ok(default));
        }
        public void CorruptIndex(string profile) => _indices[profile] = new PluginStoreSnapshot((_indices.GetValueOrDefault(profile)?.Revision ?? 0) + 1, ImmutableArray.CreateRange(Encoding.UTF8.GetBytes("{invalid")));
        public void TamperContent(string profile, PluginPackageIdentity identity) => _content[(profile, identity.Sha256)] = [1, 2, 3];
        public bool HasContent(string profile, PluginPackageIdentity identity) => _content.ContainsKey((profile, identity.Sha256));
    }

    internal sealed class UserStore : IPluginUserStorageStore
    {
        private readonly Dictionary<(string Profile, string Plugin, string Login), PluginStoreSnapshot> _values = [];
        public TaskCompletionSource? LoadEntered { get; set; }
        public TaskCompletionSource? ReleaseLoad { get; set; }
        public TaskCompletionSource? CommitEntered { get; set; }
        public TaskCompletionSource? ReleaseCommit { get; set; }
        public async Task<Result<PluginStoreSnapshot>> LoadAsync(string profile, string pluginId, string localLoginKey, CancellationToken ct)
        {
            var snapshot = _values.GetValueOrDefault((profile, pluginId, localLoginKey)) ?? new PluginStoreSnapshot(0, []);
            LoadEntered?.TrySetResult();
            if (ReleaseLoad is not null) await ReleaseLoad.Task.WaitAsync(ct);
            return Result<PluginStoreSnapshot>.Ok(snapshot);
        }
        public async Task<Result<long>> CommitAsync(string profile, string pluginId, string localLoginKey, long expectedRevision,
            ReadOnlyMemory<byte> data, Func<Func<bool>, bool> publishWhileCurrent, CancellationToken ct)
        {
            var key = (profile, pluginId, localLoginKey);
            var before = _values.GetValueOrDefault(key) ?? new PluginStoreSnapshot(0, []);
            if (before.Revision != expectedRevision) return Result<long>.Fail(new ValidationError("Synthetic conflict."));
            var next = new PluginStoreSnapshot(before.Revision + 1, ImmutableArray.CreateRange(data.ToArray()));
            if (!publishWhileCurrent(() => { _values[key] = next; return true; }))
                return Result<long>.Fail(new ValidationError("Synthetic publication refused."));
            CommitEntered?.TrySetResult();
            if (ReleaseCommit is not null) await ReleaseCommit.Task.WaitAsync(ct);
            return Result<long>.Ok(next.Revision);
        }
    }

    internal sealed class Revocation : IPluginRevocationCoordinator
    {
        public bool Pending { get; set; }
        public int Calls { get; private set; }
        public Func<Task>? OnRevoke { get; set; }
        public Result<IDisposable> Track(PluginRuntimeBinding binding, Func<CancellationToken, ValueTask> teardown) => Result<IDisposable>.Ok(new Noop());
        public async Task<Result<Unit>> RevokeAsync(PluginPackageIdentity package, long throughGeneration, CancellationToken ct)
        {
            Calls++;
            if (OnRevoke is not null) await OnRevoke();
            return Pending ? Result<Unit>.Fail(new ValidationError("Synthetic cleanup pending.")) : Result<Unit>.Ok(default);
        }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    internal sealed class Sessions : IPluginSessionSource
    {
        private readonly Dictionary<string, IAccountSession> _sessions = new(StringComparer.Ordinal) { ["login-a"] = new Session("login-a"), ["login-b"] = new Session("login-b") };
        private readonly Lock _gate = new();
        private string _active = "login-a";
        private long _generation = 1;
        public void Switch(string login) { lock (_gate) { _active = login; _generation++; } }
        public Result<PluginSessionCapture> CaptureActive()
        {
            lock (_gate) return Result<PluginSessionCapture>.Ok(new(_sessions[_active], _generation));
        }
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => _sessions.TryGetValue(localAccountKey, out var session)
            ? Result<PluginSessionCapture>.Ok(new(session, null)) : Result<PluginSessionCapture>.Fail(new ValidationError("Unknown synthetic login."));
        public bool IsCurrent(PluginSessionCapture capture)
        {
            lock (_gate) return Current(capture);
        }
        public bool TryRunWhileCurrent(IReadOnlyList<PluginSessionCapture> captures, Func<bool> write)
        {
            lock (_gate) return captures.All(Current) && write();
        }
        private bool Current(PluginSessionCapture capture) => _sessions.TryGetValue(capture.LocalAccountKey, out var session)
            && ReferenceEquals(session, capture.Session) && (capture.ActiveSelectionGeneration is null || capture.ActiveSelectionGeneration == _generation);
    }

    private sealed class Session(string key) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "sample@example.org", "https://jmap.example.org");
        public bool AllowsAddOnStorage => true;
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}
