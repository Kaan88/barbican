// Mirrors: draft-ietf-jmap-filenode in the shape of Stalwart 0.16 (nodeType directory or file, type for the media type) and, with FakeFiles.Draft18, of Stalwart 1.0 (@type, mediaType), crates/jmap/src/file/{get,query,set,changes}.rs;
// Rust reader.rs:887 folder_tree, :949 file_node. Answers only with FakeJmapOptions.FileNodeCapability. get (maxObjectsInGet), query (parentId filter, position, limit, total), set (create, update with pointer patches, destroy with
// onDestroyRemoveChildren, onExists rename), changes, shared accounts (FakeFiles.SharedAccounts). Seeded on first use: Documents > Invoices, Photos and a .Trash folder (M2), or the M5 tree (FakeSeed.FilesM5). Save to Files (T-221, J-075).
// Owner reader (M2); update, changes, onExists, move, shareWith and the draft-18 shape are files-data's (M5 plan 3.1).

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static readonly string[] ReservedFileNames = ["con", "aux", "nul", "prn"];
    private FakeFiles? _defaultFiles;

    /// <summary>The nodes of the user's own account (seeded on first use), as objects.</summary>
    public IReadOnlyList<JsonObject> FileNodes => FileNodesOf(options.AccountId);

    /// <summary>The nodes of one account (the user's own or a shared one).</summary>
    public IReadOnlyList<JsonObject> FileNodesOf(string accountId) => EnsureFiles().Nodes(accountId);

    // FileNode and Blob/copy also serve the accounts shared with the login
    private bool IsSharedFileAccount(string method, string? accountId) =>
        accountId is not null && (method.StartsWith("FileNode/", StringComparison.Ordinal) || string.Equals(method, "Blob/copy", StringComparison.Ordinal)) && options.Files is { } f && f.SharedAccounts.Contains(accountId, StringComparer.Ordinal);

    private FakeFiles EnsureFiles()
    {
        var f = options.Files ?? (_defaultFiles ??= new FakeFiles());
        lock (f.Gate)
        {
            if (f.Seeded)
            {
                return f;
            }

            f.Seeded = true;
            _ = f.Get(options.AccountId);
            foreach (var shared in f.SharedAccounts)
            {
                _ = f.Get(shared);
            }

            if (f.SeedM5)
            {
                FakeSeed.FilesM5(f, store, options.AccountId);
            }
            else
            {
                _ = f.Add(options.AccountId, null, "Documents", true, id: "D1");
                _ = f.Add(options.AccountId, "D1", "Invoices", true, id: "D2");
                _ = f.Add(options.AccountId, null, "Photos", true, id: "D3");
                _ = f.Add(options.AccountId, null, ".Trash", true, id: "D4");
            }
        }

        return f;
    }

    private (FakeFiles Files, FakeFiles.Account Account, string Id)? FileAccount(JsonObject args, out (string? Name, JsonObject Args) refusal)
    {
        refusal = default;
        if (!options.FileNodeCapability)
        {
            refusal = Error("unknownMethod");
            return null;
        }

        var f = EnsureFiles();
        if (f.Fails)
        {
            refusal = Error("serverFail");
            return null;
        }

        var id = (string?)args["accountId"] ?? options.AccountId;
        lock (f.Gate)
        {
            return (f, f.Get(id), id);
        }
    }

    private (string? Name, JsonObject Args) FileNodeGet(JsonObject args)
    {
        if (FileAccount(args, out var refusal) is not var (files, account, accountId))
        {
            return refusal;
        }

        var ids = args["ids"] is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToList() : null;
        if (ids is not null && ids.Count > options.MaxObjectsInGet)
        {
            return Error("requestTooLarge");
        }

        var props = args["properties"] is JsonArray p ? p.Select(x => (string?)x).OfType<string>().Append("id").ToHashSet(StringComparer.Ordinal) : null;
        lock (files.Gate)
        {
            var wanted = ids?.ToHashSet(StringComparer.Ordinal);
            var list = account.Nodes.Where(n => wanted is null || wanted.Contains((string)n["id"]!)).Select(n => (JsonNode)Project(n, props));
            var notFound = ids is null ? [] : ids.Where(i => !account.Nodes.Any(n => string.Equals((string?)n["id"], i, StringComparison.Ordinal))).Select(i => (JsonNode)i);
            return (null, new JsonObject { ["accountId"] = accountId, ["state"] = FileState(account), ["list"] = new JsonArray([.. list]), ["notFound"] = new JsonArray([.. notFound]) });
        }
    }

    private static JsonObject Project(JsonObject n, HashSet<string>? props)
    {
        var o = new JsonObject();
        foreach (var (k, v) in n)
        {
            if (props is null || props.Contains(k))
            {
                o[k] = v?.DeepClone();
            }
        }

        return o;
    }

    private (string? Name, JsonObject Args) FileNodeQuery(JsonObject args)
    {
        if (FileAccount(args, out var refusal) is not var (files, account, accountId))
        {
            return refusal;
        }

        var parent = args["filter"] is JsonObject f && f.ContainsKey("parentId") ? (string?)f["parentId"] ?? string.Empty : null;
        var position = args["position"] is JsonValue pv && pv.TryGetValue<int>(out var pos) ? Math.Max(0, pos) : 0;
        var limit = Math.Min(args["limit"] is JsonValue lv && lv.TryGetValue<int>(out var l) ? Math.Max(0, l) : int.MaxValue, options.QueryLimitCap);
        lock (files.Gate)
        {
            var all = account.Nodes
                .Where(n => parent is null || string.Equals((string?)n["parentId"] ?? string.Empty, parent, StringComparison.Ordinal))
                .Select(n => (string)n["id"]!)
                .ToList();
            var page = all.Skip(position).Take(limit).Select(i => (JsonNode)i);
            var res = new JsonObject { ["accountId"] = accountId, ["queryState"] = FileState(account), ["canCalculateChanges"] = false, ["position"] = position, ["ids"] = new JsonArray([.. page]) };
            if (args["calculateTotal"] is JsonValue ct && ct.TryGetValue<bool>(out var total) && total)
            {
                res["total"] = all.Count;
            }

            return (null, res);
        }
    }

    private (string? Name, JsonObject Args) FileNodeChanges(JsonObject args)
    {
        if (FileAccount(args, out var refusal) is not var (files, account, accountId))
        {
            return refusal;
        }

        var since = (string?)args["sinceState"] ?? string.Empty;
        if (!since.StartsWith('f') || !int.TryParse(since.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var from))
        {
            return Error("cannotCalculateChanges");
        }

        lock (files.Gate)
        {
            if (from < account.LogFloor || from > account.State)
            {
                return Error("cannotCalculateChanges");
            }

            var window = account.Log.Where(e => e.State > from).ToList();
            var created = new List<string>();
            var updated = new List<string>();
            var destroyed = new List<string>();
            foreach (var id in window.Select(e => e.Id).Distinct(StringComparer.Ordinal))
            {
                var kinds = window.Where(e => string.Equals(e.Id, id, StringComparison.Ordinal)).Select(e => e.Kind).ToList();
                var wasCreated = kinds.Contains("created", StringComparer.Ordinal);
                var gone = string.Equals(kinds[^1], "destroyed", StringComparison.Ordinal);
                if (gone)
                {
                    if (!wasCreated)
                    {
                        destroyed.Add(id);
                    }
                }
                else
                {
                    (wasCreated ? created : updated).Add(id);
                }
            }

            return (null, new JsonObject
            {
                ["accountId"] = accountId,
                ["oldState"] = since,
                ["newState"] = FileState(account),
                ["hasMoreChanges"] = false,
                ["created"] = new JsonArray([.. created.Select(i => (JsonNode)i)]),
                ["updated"] = new JsonArray([.. updated.Select(i => (JsonNode)i)]),
                ["destroyed"] = new JsonArray([.. destroyed.Select(i => (JsonNode)i)]),
            });
        }
    }

    private (string? Name, JsonObject Args) FileNodeSet(JsonObject args)
    {
        if (FileAccount(args, out var refusal) is not var (files, account, accountId))
        {
            return refusal;
        }

        var rename = string.Equals((string?)args["onExists"], "rename", StringComparison.Ordinal);
        var removeChildren = args["onDestroyRemoveChildren"] is JsonValue rc && rc.TryGetValue<bool>(out var rcv) && rcv;
        var created = new JsonObject();
        var notCreated = new JsonObject();
        var updated = new JsonObject();
        var notUpdated = new JsonObject();
        var destroyed = new JsonArray();
        var notDestroyed = new JsonObject();
        lock (files.Gate)
        {
            var oldState = FileState(account);
            if (args["ifInState"] is JsonValue guard && !string.Equals((string?)guard, oldState, StringComparison.Ordinal))
            {
                return Error("stateMismatch");
            }

            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in args["create"] as JsonObject ?? [])
            {
                var error = CreateFileNode(files, account, value as JsonObject, rename, ids, out var id);
                if (error is not null)
                {
                    notCreated[key] = error;
                    continue;
                }

                ids[key] = id!;
                created[key] = new JsonObject { ["id"] = id };
            }

            foreach (var (id, patch) in args["update"] as JsonObject ?? [])
            {
                var realId = id.StartsWith('#') && ids.TryGetValue(id[1..], out var made) ? made : id;
                if (UpdateFileNode(files, account, realId, patch as JsonObject, rename, ids) is { } error)
                {
                    notUpdated[id] = error;
                }
                else
                {
                    updated[id] = null;
                }
            }

            DestroyFileNodes(account, args["destroy"] as JsonArray ?? [], removeChildren, destroyed, notDestroyed);

            return (null, SetResponse(accountId, oldState, FileState(account), created, updated, destroyed, notCreated, notUpdated, notDestroyed));
        }
    }

    private static JsonObject SetResponse(string accountId, string oldState, string newState, JsonObject created, JsonObject updated, JsonArray destroyed, JsonObject notCreated, JsonObject notUpdated, JsonObject notDestroyed) => new()
    {
        ["accountId"] = accountId,
        ["oldState"] = oldState,
        ["newState"] = newState,
        ["created"] = created.Count == 0 ? null : created,
        ["updated"] = updated.Count == 0 ? null : updated,
        ["destroyed"] = destroyed.Count == 0 ? null : destroyed,
        ["notCreated"] = notCreated.Count == 0 ? null : notCreated,
        ["notUpdated"] = notUpdated.Count == 0 ? null : notUpdated,
        ["notDestroyed"] = notDestroyed.Count == 0 ? null : notDestroyed,
    };

    private static void DestroyFileNodes(FakeFiles.Account account, JsonArray ids, bool removeChildren, JsonArray destroyed, JsonObject notDestroyed)
    {
        foreach (var id in ids.Select(x => (string?)x).OfType<string>())
        {
            if (!account.Nodes.Exists(n => string.Equals((string?)n["id"], id, StringComparison.Ordinal)))
            {
                notDestroyed[id] = new JsonObject { ["type"] = "notFound" };
                continue;
            }

            var subtree = Subtree(account, id);
            if (subtree.Count > 1 && !removeChildren)
            {
                notDestroyed[id] = new JsonObject { ["type"] = "nodeHasChildren" };
                continue;
            }

            foreach (var gone in subtree)
            {
                _ = account.Nodes.RemoveAll(n => string.Equals((string?)n["id"], gone, StringComparison.Ordinal));
                LogFile(account, gone, "destroyed");
            }

            destroyed.Add(id);
        }
    }

    // A file needs a known blob, a name and an existing parent directory (or none); a name already used in that folder is alreadyExists, or made unique with onExists rename.
    private JsonObject? CreateFileNode(FakeFiles files, FakeFiles.Account account, JsonObject? create, bool rename, Dictionary<string, string> ids, out string? id)
    {
        id = null;
        var name = (string?)create?["name"];
        var parent = (string?)create?["parentId"];
        if (parent is not null && parent.StartsWith('#') && ids.TryGetValue(parent[1..], out var made))
        {
            parent = made;
        }

        var blobId = (string?)create?["blobId"];
        if (create is null || NameProblem(files, name) is not null)
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("name") };
        }

        if (files.Draft18 && create["@type"] is null)
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("@type") };
        }

        var directory = files.Draft18 ? string.Equals((string?)create["@type"], "Directory", StringComparison.Ordinal) : blobId is null;
        if (parent is not null && !account.Nodes.Exists(n => string.Equals((string?)n["id"], parent, StringComparison.Ordinal) && FakeFiles.IsDir(n)))
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("parentId") };
        }

        if (!directory && (blobId is null || store.Blob(blobId) is null))
        {
            return new JsonObject { ["type"] = "blobNotFound" };
        }

        var finalName = name!;
        if (Taken(account, parent, finalName, null))
        {
            if (!rename)
            {
                return new JsonObject { ["type"] = "alreadyExists" };
            }

            finalName = FreeName(account, parent, finalName, null);
        }

        id = "F" + (++account.NextId).ToString(CultureInfo.InvariantCulture);
        var type = (string?)create["type"] ?? (string?)create["mediaType"];
        account.Nodes.Add(files.MakeNode(id, parent, finalName, directory, blobId, type, directory ? 0 : store.Blob(blobId!)!.Length, Now, create["shareWith"] as JsonObject));
        LogFile(account, id, "created");
        return null;
    }

    private JsonObject? UpdateFileNode(FakeFiles files, FakeFiles.Account account, string id, JsonObject? patch, bool rename, Dictionary<string, string> ids)
    {
        var node = account.Nodes.Find(n => string.Equals((string?)n["id"], id, StringComparison.Ordinal));
        if (node is null)
        {
            return new JsonObject { ["type"] = "notFound" };
        }

        if (patch is null)
        {
            return new JsonObject { ["type"] = "invalidPatch" };
        }

        var working = (JsonObject)node.DeepClone();
        foreach (var (key, value) in patch)
        {
            if (ApplyPatchKey(files, account, id, working, key, value, ids) is { } error)
            {
                return error;
            }
        }

        var finalName = (string)working["name"]!;
        var finalParent = (string?)working["parentId"];
        if (Taken(account, finalParent, finalName, id))
        {
            if (!rename)
            {
                return new JsonObject { ["type"] = "alreadyExists" };
            }

            working["name"] = FreeName(account, finalParent, finalName, id);
        }

        working["modified"] = Now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
        account.Nodes[account.Nodes.IndexOf(node)] = working;
        LogFile(account, id, "updated");
        return null;
    }

    // one key of an update patch applied to the working copy of a node; the SetError when the value is refused
    private JsonObject? ApplyPatchKey(FakeFiles files, FakeFiles.Account account, string id, JsonObject working, string key, JsonNode? value, Dictionary<string, string> ids)
    {
        switch (key)
        {
            case "name":
                if (NameProblem(files, (string?)value) is not null)
                {
                    return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("name") };
                }

                working["name"] = (string?)value;
                return null;
            case "parentId":
                var parent = (string?)value;
                if (parent is not null && parent.StartsWith('#') && ids.TryGetValue(parent[1..], out var made))
                {
                    parent = made;
                }

                if (parent is not null && (!account.Nodes.Exists(n => string.Equals((string?)n["id"], parent, StringComparison.Ordinal) && FakeFiles.IsDir(n)) || Subtree(account, id).Contains(parent, StringComparer.Ordinal)))
                {
                    return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("parentId") };
                }

                working["parentId"] = parent;
                return null;
            case "blobId":
                var blob = (string?)value;
                if (blob is null || store.Blob(blob) is not { } bytes)
                {
                    return new JsonObject { ["type"] = "blobNotFound" };
                }

                working["blobId"] = blob;
                working["size"] = bytes.Length;
                return null;
            case "type" or "mediaType":
                working[files.Draft18 ? "mediaType" : "type"] = (string?)value;
                return null;
            case "shareWith":
                working["shareWith"] = value?.DeepClone();
                return null;
            default:
                if (!key.StartsWith("shareWith/", StringComparison.Ordinal))
                {
                    return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray(key) };
                }

                PatchShare(working, key, value);
                return null;
        }
    }

    // shareWith/<principal> (RFC 6901 escapes): a value sets the rights of one principal, null removes them
    private static void PatchShare(JsonObject working, string key, JsonNode? value)
    {
        var principal = key["shareWith/".Length..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        var share = working["shareWith"] as JsonObject ?? [];
        if (value is null)
        {
            _ = share.Remove(principal);
        }
        else
        {
            share[principal] = value.DeepClone();
        }

        working["shareWith"] = share.Count == 0 ? null : share;
    }

    private static string? NameProblem(FakeFiles files, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "empty";
        }

        if (!files.NameRules)
        {
            return null;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(name) > 255)
        {
            return "too long";
        }

        if (name.Any(c => FakeFiles.ForbiddenChars.Contains(c, StringComparison.Ordinal) || c <= '\u001f' || (c >= '\u007f' && c <= '\u009f')))
        {
            return "forbidden character";
        }

        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot > 0 ? name[..dot] : name).ToLowerInvariant();
        return ReservedFileNames.Contains(stem, StringComparer.Ordinal) ? "reserved" : null;
    }

    private static bool Taken(FakeFiles.Account account, string? parent, string name, string? exceptId) =>
        account.Nodes.Exists(n => string.Equals((string?)n["parentId"], parent, StringComparison.Ordinal) && string.Equals((string?)n["name"], name, StringComparison.Ordinal) && !string.Equals((string?)n["id"], exceptId, StringComparison.Ordinal));

    private static string FreeName(FakeFiles.Account account, string? parent, string name, string? exceptId)
    {
        var dot = name.LastIndexOf('.');
        var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, string.Empty);
        for (var i = 1; ; i++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} ({i}){ext}");
            if (!Taken(account, parent, candidate, exceptId))
            {
                return candidate;
            }
        }
    }

    private static List<string> Subtree(FakeFiles.Account account, string id)
    {
        var all = new List<string> { id };
        for (var i = 0; i < all.Count; i++)
        {
            var parent = all[i];
            all.AddRange(account.Nodes.Where(n => string.Equals((string?)n["parentId"], parent, StringComparison.Ordinal)).Select(n => (string)n["id"]!).Where(c => !all.Contains(c, StringComparer.Ordinal)));
        }

        return all;
    }

    private static void LogFile(FakeFiles.Account account, string id, string kind)
    {
        account.State++;
        account.Log.Add((account.State, id, kind));
    }

    private static string FileState(FakeFiles.Account account) => "f" + account.State.ToString(CultureInfo.InvariantCulture);
}
