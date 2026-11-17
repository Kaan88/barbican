// Mirrors: draft-ietf-jmap-filenode in the shape of Stalwart 0.16 (nodeType directory or file, type for the media type), crates/jmap/src/file/{get,query,set}.rs;
// Rust reader.rs:887 folder_tree, :949 file_node. Answers only with FakeJmapOptions.FileNodeCapability; seeded with Documents > Invoices, Photos and
// a .Trash folder on first use. Save to Files (T-221, J-075 partial). Owner reader.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<JsonObject> _fileNodes = [];
    private int _fileState;

    /// <summary>The FileNode store (seeded on first use), as objects.</summary>
    public IReadOnlyList<JsonObject> FileNodes
    {
        get
        {
            lock (_lock)
            {
                SeedFileNodes();
                return [.. _fileNodes.Select(n => (JsonObject)n.DeepClone())];
            }
        }
    }

    private (string? Name, JsonObject Args) FileNodeGet(JsonObject args)
    {
        if (!options.FileNodeCapability)
        {
            return Error("unknownMethod");
        }

        lock (_lock)
        {
            SeedFileNodes();
            var ids = args["ids"] is JsonArray a ? a.Select(x => (string?)x).OfType<string>().ToHashSet(StringComparer.Ordinal) : null;
            var list = _fileNodes.Where(n => ids is null || ids.Contains((string)n["id"]!)).Select(n => (JsonNode)n.DeepClone());
            var notFound = ids is null ? [] : ids.Where(i => !_fileNodes.Any(n => string.Equals((string?)n["id"], i, StringComparison.Ordinal))).Select(i => (JsonNode)i);
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = FileState(), ["list"] = new JsonArray([.. list]), ["notFound"] = new JsonArray([.. notFound]) });
        }
    }

    private (string? Name, JsonObject Args) FileNodeQuery(JsonObject args)
    {
        if (!options.FileNodeCapability)
        {
            return Error("unknownMethod");
        }

        lock (_lock)
        {
            SeedFileNodes();
            var parent = args["filter"] is JsonObject f && f.ContainsKey("parentId") ? (string?)f["parentId"] ?? string.Empty : null;
            var ids = _fileNodes
                .Where(n => parent is null || string.Equals((string?)n["parentId"] ?? string.Empty, parent, StringComparison.Ordinal))
                .Select(n => (JsonNode)(string)n["id"]!);
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["queryState"] = FileState(), ["canCalculateChanges"] = false, ["position"] = 0, ["ids"] = new JsonArray([.. ids]) });
        }
    }

    private (string? Name, JsonObject Args) FileNodeSet(JsonObject args)
    {
        if (!options.FileNodeCapability)
        {
            return Error("unknownMethod");
        }

        var created = new JsonObject();
        var notCreated = new JsonObject();
        var destroyed = new JsonArray();
        lock (_lock)
        {
            SeedFileNodes();
            var oldState = FileState();
            foreach (var (key, value) in args["create"] as JsonObject ?? [])
            {
                if (CreateFileNode(value as JsonObject) is { } error)
                {
                    notCreated[key] = error;
                }
                else
                {
                    created[key] = new JsonObject { ["id"] = (string?)_fileNodes[^1]["id"] };
                }
            }

            foreach (var id in (args["destroy"] as JsonArray ?? []).Select(x => (string?)x).OfType<string>())
            {
                if (_fileNodes.RemoveAll(n => string.Equals((string?)n["id"], id, StringComparison.Ordinal)) > 0)
                {
                    destroyed.Add(id);
                }
            }

            return (null, new JsonObject
            {
                ["accountId"] = options.AccountId,
                ["oldState"] = oldState,
                ["newState"] = FileState(),
                ["created"] = created,
                ["notCreated"] = notCreated,
                ["destroyed"] = destroyed,
            });
        }
    }

    // A file needs a known blob, a name, and an existing parent directory (or none); a name already used in that folder is alreadyExists.
    private JsonObject? CreateFileNode(JsonObject? create)
    {
        var name = (string?)create?["name"];
        var parent = (string?)create?["parentId"];
        var blobId = (string?)create?["blobId"];
        if (create is null || string.IsNullOrWhiteSpace(name))
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("name") };
        }

        if (parent is not null && !_fileNodes.Any(n => string.Equals((string?)n["id"], parent, StringComparison.Ordinal) && string.Equals((string?)n["nodeType"], "directory", StringComparison.Ordinal)))
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("parentId") };
        }

        if (blobId is not null && store.Blob(blobId) is null)
        {
            return new JsonObject { ["type"] = "blobNotFound" };
        }

        if (_fileNodes.Any(n => string.Equals((string?)n["parentId"], parent, StringComparison.Ordinal) && string.Equals((string?)n["name"], name, StringComparison.Ordinal)))
        {
            return new JsonObject { ["type"] = "alreadyExists" };
        }

        var id = "F" + (_fileNodes.Count + 1).ToString(CultureInfo.InvariantCulture);
        _fileNodes.Add(new JsonObject
        {
            ["id"] = id,
            ["parentId"] = parent,
            ["name"] = name,
            ["nodeType"] = blobId is null ? "directory" : "file",
            ["blobId"] = blobId,
            ["type"] = blobId is null ? null : (string?)create["type"] ?? (string?)create["mediaType"] ?? "application/octet-stream",
            ["size"] = blobId is null ? null : store.Blob(blobId)!.Length,
        });
        _fileState++;
        return null;
    }

    private string FileState() => "f" + _fileState.ToString(CultureInfo.InvariantCulture);

    private void SeedFileNodes()
    {
        if (_fileNodes.Count > 0)
        {
            return;
        }

        static JsonObject Dir(string id, string name, string? parent) => new() { ["id"] = id, ["parentId"] = parent, ["name"] = name, ["nodeType"] = "directory" };
        _fileNodes.Add(Dir("D1", "Documents", null));
        _fileNodes.Add(Dir("D2", "Invoices", "D1"));
        _fileNodes.Add(Dir("D3", "Photos", null));
        _fileNodes.Add(Dir("D4", ".Trash", null));
    }
}
