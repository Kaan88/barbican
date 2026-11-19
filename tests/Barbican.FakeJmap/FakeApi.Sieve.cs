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
    private int _scriptState;
    private bool _refuseNextSieveSet;
    private bool _skipNextSieveActivation;
    private bool _wrongNextSieveAcknowledgement;

    /// <summary>Return a per-script refusal with an empty updated map for the next set.</summary>
    public void RefuseNextSieveSet() { lock (_lock) { _refuseNextSieveSet = true; } }

    /// <summary>Simulate a server that acknowledges a set but fails to activate the script.</summary>
    public void SkipNextSieveActivation() { lock (_lock) { _skipNextSieveActivation = true; } }

    /// <summary>Return an unrelated updated id without acknowledging the requested target.</summary>
    public void WrongNextSieveAcknowledgement() { lock (_lock) { _wrongNextSieveAcknowledgement = true; } }

    private string SieveState => "s" + _scriptState.ToString(CultureInfo.InvariantCulture);

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
            _scriptState++;
        }
    }

    /// <summary>Returns a named synthetic script's text for isolated test verification.</summary>
    public string? SieveText(string name)
    {
        lock (_lock)
        {
            var script = _scripts.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
            return script.Id is null ? null : Encoding.UTF8.GetString(store.Blob(script.BlobId) ?? []);
        }
    }

    // Stalwart 0.16.27 vacation/set operates on SyncCollection::SieveScript and activates its reserved script.
    private void ApplyVacationSieveState()
    {
        var index = _scripts.FindIndex(script => string.Equals(script.Name, "vacation", StringComparison.Ordinal));
        if (index < 0)
        {
            var id = (++_scriptSeq).ToString(CultureInfo.InvariantCulture);
            _scripts.Add((id, "vacation", store.PutBlob(Encoding.UTF8.GetBytes("# fake reserved vacation")), false));
            index = _scripts.Count - 1;
        }

        if (CurrentVacation.IsEnabled)
        {
            var id = _scripts[index].Id;
            for (var i = 0; i < _scripts.Count; i++) _scripts[i] = _scripts[i] with { Active = string.Equals(_scripts[i].Id, id, StringComparison.Ordinal) };
        }
        else if (_scripts[index].Active) _scripts[index] = _scripts[index] with { Active = false };
        _scriptState++;
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

            return (null, new JsonObject { ["accountId"] = options.AccountId, ["state"] = SieveState, ["list"] = list, ["notFound"] = notFound });
        }
    }

    private (string? Name, JsonObject Args) SieveScriptSet(JsonObject args)
    {
        lock (_lock)
        {
            var oldState = SieveState;
            if ((string?)args["ifInState"] is { } expected && !string.Equals(expected, oldState, StringComparison.Ordinal))
                return Error("stateMismatch");
            if (SieveFault(args, oldState) is { } injected) return injected;

            var oldActive = _scripts.FirstOrDefault(script => script.Active).Id;
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

            UpdateSieveScripts(args, updated, notUpdated);

            if (_skipNextSieveActivation) _skipNextSieveActivation = false;
            else ActivateLocked((string?)args["onSuccessActivateScript"], refs);
            var newActive = _scripts.FirstOrDefault(script => script.Active).Id;
            if (created.Count > 0 || updated.Count > 0 || !string.Equals(oldActive, newActive, StringComparison.Ordinal)) _scriptState++;
            var res = new JsonObject { ["accountId"] = options.AccountId, ["oldState"] = oldState, ["newState"] = SieveState };
            res["created"] = created.Count > 0 ? created : null;
            res["updated"] = updated.Count > 0 ? updated : null;
            res["notCreated"] = notCreated.Count > 0 ? notCreated : null;
            res["notUpdated"] = notUpdated.Count > 0 ? notUpdated : null;
            return (null, res);
        }
    }

    private void UpdateSieveScripts(JsonObject args, JsonObject updated, JsonObject notUpdated)
    {
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
    }

    private (string? Name, JsonObject Args)? SieveFault(JsonObject args, string oldState)
    {
        if (_refuseNextSieveSet)
        {
            _refuseNextSieveSet = false;
            var updateId = (args["update"] as JsonObject)?.FirstOrDefault().Key;
            var createId = (args["create"] as JsonObject)?.FirstOrDefault().Key;
            var key = updateId ?? createId ?? "n";
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["oldState"] = oldState, ["newState"] = oldState,
                ["updated"] = new JsonObject(), [updateId is null ? "notCreated" : "notUpdated"] = new JsonObject { [key] = new JsonObject { ["type"] = "forbidden", ["description"] = "fixture refusal" } } });
        }

        if (_wrongNextSieveAcknowledgement)
        {
            _wrongNextSieveAcknowledgement = false;
            return (null, new JsonObject { ["accountId"] = options.AccountId, ["oldState"] = oldState, ["newState"] = oldState,
                ["updated"] = new JsonObject { ["unrelated"] = null } });
        }

        return null;
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
