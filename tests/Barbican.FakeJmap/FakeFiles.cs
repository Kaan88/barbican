// An in-memory file storage for the fake server (M5 plan section 4.13: update, FileNode/changes, onExists rename, move, shareWith and the draft-18 shape behind an option). Mirrors draft-ietf-jmap-filenode in the two shapes
// Stalwart serves (0.16: nodeType directory or file, type; 1.0 / draft 18: @type Directory or File, mediaType), Stalwart crates/jmap/src/file/{get,query,set,changes}.rs (a state string per type, ids paged by position, maxObjectsInGet enforced,
// name rules of the filenode capability, onExists, onDestroyRemoveChildren). Owner files-data.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>The file nodes of the user's account and of accounts shared with the login, in one shape (0.16 or draft 18).</summary>
public sealed class FakeFiles
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Account> _accounts = new(StringComparer.Ordinal);

    /// <summary>Serve the draft-18 shape (@type, mediaType) and advertise supportedNodeTypes; a create without @type is refused.</summary>
    public bool Draft18 { get; init; }

    /// <summary>Publish forbiddenNameChars, forbiddenNodeNames and maxSizeFileNodeName in the capability and refuse names that break them (Stalwart 0.16.6+).</summary>
    public bool NameRules { get; init; }

    /// <summary>Seed the M5 tree (<see cref="FakeSeed.FilesM5"/>) instead of the M2 one (Documents, Photos, .Trash).</summary>
    public bool SeedM5 { get; init; }

    /// <summary>Accounts shared with the login (the session lists them with the filenode capability); seeded with a small tree when <see cref="SeedM5"/>.</summary>
    public IReadOnlyList<string> SharedAccounts { get; init; } = [];

    /// <summary>With <see cref="SeedM5"/>: this many files in a folder "Bulk" of the user's account (3 000 exercises the chunked get).</summary>
    public int BulkNodes { get; init; }

    /// <summary>Answer every FileNode method with serverFail (a failing server).</summary>
    public bool Fails { get; set; }

    /// <summary>True once the first request seeded the trees.</summary>
    internal bool Seeded { get; set; }

    /// <summary>Account ids that have nodes.</summary>
    public IReadOnlyList<string> AccountIds
    {
        get
        {
            lock (_lock)
            {
                return [.. _accounts.Keys];
            }
        }
    }

    /// <summary>The names the capability forbids.</summary>
    public static string ForbiddenChars => "/\\:*?\"<>|";

    /// <summary>A copy of the nodes of an account, in insertion order.</summary>
    public IReadOnlyList<JsonObject> Nodes(string accountId)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(accountId, out var a) ? [.. a.Nodes.Select(n => (JsonObject)n.DeepClone())] : [];
        }
    }

    /// <summary>The state string of an account.</summary>
    public string State(string accountId)
    {
        lock (_lock)
        {
            return _accounts.TryGetValue(accountId, out var a) ? "f" + a.State.ToString(CultureInfo.InvariantCulture) : "f0";
        }
    }

    /// <summary>Forgets the change log: the next /changes from any older state answers cannotCalculateChanges.</summary>
    public void ExpireStates()
    {
        lock (_lock)
        {
            foreach (var a in _accounts.Values)
            {
                a.LogFloor = a.State;
                a.Log.Clear();
            }
        }
    }

    /// <summary>Adds a node (seeding); returns its id. Seeding does not move the state.</summary>
    public string Add(string accountId, string? parentId, string name, bool directory, string? blobId = null, string? type = null, long size = 0, string? id = null, JsonObject? shareWith = null, JsonObject? myRights = null)
    {
        lock (_lock)
        {
            var a = Get(accountId);
            var nid = id ?? "F" + (++a.NextId).ToString(CultureInfo.InvariantCulture);
            a.Nodes.Add(MakeNode(nid, parentId, name, directory, blobId, type, size, DateTimeOffset.UnixEpoch.AddDays(20_000), shareWith, myRights));
            return nid;
        }
    }

    /// <summary>The raw node (not a copy) or null; the caller holds <see cref="Lock"/>.</summary>
    internal Lock Gate => _lock;

    internal bool Has(string accountId)
    {
        lock (_lock)
        {
            return _accounts.ContainsKey(accountId);
        }
    }

    internal Account Get(string accountId)
    {
        if (!_accounts.TryGetValue(accountId, out var a))
        {
            _accounts[accountId] = a = new Account();
        }

        return a;
    }

    internal JsonObject MakeNode(string id, string? parentId, string name, bool directory, string? blobId, string? type, long size, DateTimeOffset now, JsonObject? shareWith = null, JsonObject? myRights = null)
    {
        var stamp = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var n = new JsonObject { ["id"] = id, ["parentId"] = parentId, ["name"] = name };
        if (Draft18)
        {
            n["@type"] = directory ? "Directory" : "File";
            n["mediaType"] = directory ? null : type ?? "application/octet-stream";
        }
        else
        {
            n["nodeType"] = directory ? "directory" : "file";
            n["type"] = directory ? null : type ?? "application/octet-stream";
        }

        n["blobId"] = directory ? null : blobId;
        n["size"] = directory ? null : size;
        n["created"] = stamp;
        n["modified"] = stamp;
        if (shareWith is not null)
        {
            n["shareWith"] = shareWith;
        }

        if (myRights is not null)
        {
            n["myRights"] = myRights;
        }

        return n;
    }

    internal static bool IsDir(JsonObject n) => string.Equals((string?)n["nodeType"], "directory", StringComparison.Ordinal) || string.Equals((string?)n["@type"], "Directory", StringComparison.Ordinal);

    /// <summary>One account's nodes, counters and change log.</summary>
    internal sealed class Account
    {
        public List<JsonObject> Nodes { get; } = [];

        public int State { get; set; }

        public int LogFloor { get; set; }

        public int NextId { get; set; }

        /// <summary>(state after the change, id, created / updated / destroyed)</summary>
        public List<(int State, string Id, string Kind)> Log { get; } = [];
    }
}
