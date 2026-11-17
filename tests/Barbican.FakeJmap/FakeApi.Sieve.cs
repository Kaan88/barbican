// Mirrors: Stalwart crates/jmap/src/sieve/{get,set,validate}.rs (SieveScript/get with the blob, /set create and update with onSuccessActivateScript, /validate of an uploaded blob).
// The fake keeps its scripts in memory; a script with unbalanced braces or the word fake-invalid-sieve does not validate (the real server compiles it).

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<(string Id, string Name, string BlobId, bool Active)> _scripts = [];
    private int _scriptSeq;

    /// <summary>The text of the active script (null when there is none), for tests.</summary>
    public string? ActiveSieveText()
    {
        lock (_lock)
        {
            var a = _scripts.FirstOrDefault(s => s.Active);
            return a.Id is null ? null : Encoding.UTF8.GetString(store.Blob(a.BlobId) ?? []);
        }
    }

    /// <summary>Puts a script in place without a request (a test starts from a script the user wrote).</summary>
    public void SeedSieve(string name, string text, bool active)
    {
        lock (_lock)
        {
            var id = (++_scriptSeq).ToString(CultureInfo.InvariantCulture);
            _scripts.Add((id, name, store.PutBlob(Encoding.UTF8.GetBytes(text)), active));
        }
    }

    private static string? SieveError(string text)
    {
        if (text.Contains("fake-invalid-sieve", StringComparison.Ordinal))
        {
            return "syntax error near fake-invalid-sieve";
        }

        var depth = 0;
        foreach (var c in text)
        {
            depth += c == '{' ? 1 : c == '}' ? -1 : 0;
            if (depth < 0)
            {
                break;
            }
        }

        return depth == 0 ? null : "unbalanced braces";
    }

    private JsonObject ScriptJson(int i) => new()
    {
        ["id"] = _scripts[i].Id,
        ["name"] = _scripts[i].Name,
        ["blobId"] = _scripts[i].BlobId,
        ["isActive"] = _scripts[i].Active,
    };

    private (string? Name, JsonObject Args) SieveScriptGet(JsonObject args)
    {
        lock (_lock)
        {
            var wanted = Ids(args);
            var list = new JsonArray();
            var notFound = new JsonArray();
            for (var i = 0; i < _scripts.Count; i++)
            {
                if (wanted is null || wanted.Contains(_scripts[i].Id, StringComparer.Ordinal))
                {
                    list.Add(ScriptJson(i));
                }
            }

            foreach (var id in wanted ?? [])
            {
                if (!_scripts.Exists(s => string.Equals(s.Id, id, StringComparison.Ordinal)))
                {
                    notFound.Add(id);
                }
            }

            return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = "s" + _scripts.Count.ToString(CultureInfo.InvariantCulture), ["list"] = list, ["notFound"] = notFound });
        }
    }

    private (string? Name, JsonObject Args) SieveScriptSet(JsonObject args)
    {
        lock (_lock)
        {
            var created = new JsonObject();
            var notCreated = new JsonObject();
            var updated = new JsonObject();
            var notUpdated = new JsonObject();
            var refs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, v) in args["create"] as JsonObject ?? [])
            {
                var blob = (string?)v?["blobId"];
                if (blob is null || store.Blob(blob) is not { } bytes || SieveError(Encoding.UTF8.GetString(bytes)) is not null)
                {
                    notCreated[key] = new JsonObject { ["type"] = "invalidScript", ["description"] = blob is null ? "blobId is required" : "script does not compile" };
                    continue;
                }

                var id = (++_scriptSeq).ToString(CultureInfo.InvariantCulture);
                _scripts.Add((id, (string?)v?["name"] ?? "script", blob, false));
                refs[key] = id;
                created[key] = new JsonObject { ["id"] = id };
            }

            foreach (var (id, v) in args["update"] as JsonObject ?? [])
            {
                var i = _scripts.FindIndex(s => string.Equals(s.Id, id, StringComparison.Ordinal));
                if (i < 0)
                {
                    notUpdated[id] = new JsonObject { ["type"] = "notFound" };
                    continue;
                }

                var blob = (string?)v?["blobId"] ?? _scripts[i].BlobId;
                if (store.Blob(blob) is not { } bytes || SieveError(Encoding.UTF8.GetString(bytes)) is not null)
                {
                    notUpdated[id] = new JsonObject { ["type"] = "invalidScript", ["description"] = "script does not compile" };
                    continue;
                }

                _scripts[i] = _scripts[i] with { BlobId = blob, Name = (string?)v?["name"] ?? _scripts[i].Name };
                updated[id] = null;
            }

            ActivateLocked((string?)args["onSuccessActivateScript"], refs);
            var res = new JsonObject { ["accountId"] = options.AccountId, ["oldState"] = "s0", ["newState"] = "s" + _scripts.Count.ToString(CultureInfo.InvariantCulture) };
            res["created"] = created.Count > 0 ? created : null;
            res["updated"] = updated.Count > 0 ? updated : null;
            res["notCreated"] = notCreated.Count > 0 ? notCreated : null;
            res["notUpdated"] = notUpdated.Count > 0 ? notUpdated : null;
            return (null, res);
        }
    }

    private void ActivateLocked(string? act, Dictionary<string, string> refs)
    {
        if (act is null)
        {
            return;
        }

        var target = act.StartsWith('#') ? refs.GetValueOrDefault(act[1..]) : act;
        if (target is null || !_scripts.Exists(s => string.Equals(s.Id, target, StringComparison.Ordinal)))
        {
            return;
        }

        for (var i = 0; i < _scripts.Count; i++)
        {
            _scripts[i] = _scripts[i] with { Active = string.Equals(_scripts[i].Id, target, StringComparison.Ordinal) };
        }
    }

    private (string? Name, JsonObject Args) SieveScriptValidate(JsonObject args)
    {
        var blob = (string?)args["blobId"];
        var text = blob is null || store.Blob(blob) is not { } bytes ? null : Encoding.UTF8.GetString(bytes);
        var why = text is null ? "blob not found" : SieveError(text);
        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["error"] = why is null ? null : new JsonObject { ["type"] = "invalidScript", ["description"] = why },
        });
    }
}
