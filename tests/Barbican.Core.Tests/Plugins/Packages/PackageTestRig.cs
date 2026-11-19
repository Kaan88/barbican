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
    public PluginScopedStorage Storage(PluginPackageManager manager, string profile = Profile) => new(profile, manager, Accounts, Data);

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
        private readonly Dictionary<(string Profile, PluginPackageIdentity Id), ImmutableArray<byte>> _content = [];
        public bool FailBeforeCommit { get; set; }
        public bool ThrowOnCommit { get; set; }
        public bool FailAfterCommit { get; set; }
        public bool BadAuthentication { get; set; }
        public int Deletes { get; private set; }
        public int Loads { get; private set; }
        public TaskCompletionSource? LoadEntered { get; set; }
        public TaskCompletionSource? ReleaseLoad { get; set; }
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
        {
            if (ThrowOnCommit) throw new IOException("Synthetic storage failure.");
            var before = _indices.GetValueOrDefault(profile) ?? new PluginStoreSnapshot(0, []);
            if (FailBeforeCommit || before.Revision != expectedRevision) return Task.FromResult(Result<long>.Fail(new ValidationError("Synthetic commit failure.")));
            var next = new PluginStoreSnapshot(before.Revision + 1, ImmutableArray.CreateRange(index.ToArray()));
            _indices[profile] = next;
            return Task.FromResult(FailAfterCommit ? Result<long>.Fail(new ValidationError("Synthetic uncertain acknowledgement.")) : Result<long>.Ok(next.Revision));
        }
        public Task<Result<Unit>> PutContentAsync(string profile, PluginPackageIdentity identity, ReadOnlyMemory<byte> archive, CancellationToken ct)
        {
            _content[(profile, identity)] = ImmutableArray.CreateRange(archive.ToArray());
            return Task.FromResult(Result<Unit>.Ok(default));
        }
        public Task<Result<ImmutableArray<byte>>> ReadContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct) =>
            Task.FromResult(_content.TryGetValue((profile, identity), out var bytes) ? Result<ImmutableArray<byte>>.Ok(bytes) : Result<ImmutableArray<byte>>.Fail(new ValidationError("Missing synthetic content.")));
        public Task<Result<Unit>> DeleteContentAsync(string profile, PluginPackageIdentity identity, CancellationToken ct)
        {
            Deletes++;
            _content.Remove((profile, identity));
            return Task.FromResult(Result<Unit>.Ok(default));
        }
        public void CorruptIndex(string profile) => _indices[profile] = new PluginStoreSnapshot((_indices.GetValueOrDefault(profile)?.Revision ?? 0) + 1, ImmutableArray.CreateRange(Encoding.UTF8.GetBytes("{invalid")));
        public void TamperContent(string profile, PluginPackageIdentity identity) => _content[(profile, identity)] = [1, 2, 3];
        public bool HasContent(string profile, PluginPackageIdentity identity) => _content.ContainsKey((profile, identity));
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
        public async Task<Result<long>> CommitAsync(string profile, string pluginId, string localLoginKey, long expectedRevision, ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            var key = (profile, pluginId, localLoginKey);
            var before = _values.GetValueOrDefault(key) ?? new PluginStoreSnapshot(0, []);
            if (before.Revision != expectedRevision) return Result<long>.Fail(new ValidationError("Synthetic conflict."));
            var next = new PluginStoreSnapshot(before.Revision + 1, ImmutableArray.CreateRange(data.ToArray()));
            _values[key] = next;
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
        private string _active = "login-a";
        private long _generation = 1;
        public void Switch(string login) { _active = login; _generation++; }
        public Result<PluginSessionCapture> CaptureActive() => Result<PluginSessionCapture>.Ok(new(_sessions[_active], _generation));
        public Result<PluginSessionCapture> CaptureOwner(string localAccountKey) => _sessions.TryGetValue(localAccountKey, out var session)
            ? Result<PluginSessionCapture>.Ok(new(session, null)) : Result<PluginSessionCapture>.Fail(new ValidationError("Unknown synthetic login."));
        public bool IsCurrent(PluginSessionCapture capture) => _sessions.TryGetValue(capture.LocalAccountKey, out var session)
            && ReferenceEquals(session, capture.Session) && (capture.ActiveSelectionGeneration is null || capture.ActiveSelectionGeneration == _generation);
    }

    private sealed class Session(string key) : IAccountSession
    {
        public AccountInfo Account { get; } = new(key, "sample@example.org", "https://jmap.example.org");
        public IJmapClient Client => throw new NotSupportedException();
        public IMailService Mail => throw new NotSupportedException();
        public IMailSync Sync => throw new NotSupportedException();
    }
}
