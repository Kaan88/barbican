// Mirrors: Rust quick_rules.rs:229 save (SieveScript/get, the blob download), server_settings.rs:72 save_script (upload, SieveScript/validate, SieveScript/set with
// onSuccessActivateScript); Bulwark lib/jmap/client.ts:7533-7708 (the Sieve methods); RFC 9661 (JMAP for Sieve Scripts); Stalwart crates/jmap/src/sieve/{get,set,validate}.rs. J-073.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Jmap.Requests;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Sieve;

/// <summary>Reads and saves the account's Sieve script over JMAP.</summary>
public sealed class SieveScriptStore(IJmapClient client)
{
    /// <summary>The JMAP capability of Sieve scripts.</summary>
    public const string Capability = "urn:ietf:params:jmap:sieve";

    private static readonly string[] ScriptProperties = ["id", "name", "blobId", "isActive"];

    /// <summary>Whether the session advertises Sieve at all.</summary>
    public bool Available => client.Session.Has(Capability);

    /// <summary>The scripts and the active one's text.</summary>
    public async Task<Result<SieveScriptState>> ReadAsync(CancellationToken ct)
    {
        if (!Available)
        {
            return Result<SieveScriptState>.Fail(new ValidationError("This server has no Sieve filters."));
        }

        var acc = client.Session.AccountFor(Capability);
        var args = new JsonObject { ["accountId"] = acc, ["ids"] = null, ["properties"] = new JsonArray([.. ScriptProperties.Select(p => (JsonNode?)JsonValue.Create(p))]) };
        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("SieveScript/get", args, "s")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<SieveScriptState>.Fail(r.Error!);
        }

        var scripts = (r.Value!.MethodResponses[0][1].Arr("list")?.OfType<JsonObject>() ?? [])
            .Select(o => new SieveScriptInfo(o.Str("id") ?? string.Empty, o.Str("name") ?? string.Empty, o.Str("blobId") ?? string.Empty, o["isActive"] is JsonValue ia && ia.TryGetValue<bool>(out var active) && active))
            .ToList();
        var state = new SieveScriptState(scripts, string.Empty, Extensions(acc)) { State = r.Value.MethodResponses[0][1].Str("state") };
        if (state.Target is not { BlobId.Length: > 0 } target)
        {
            return Result<SieveScriptState>.Ok(state);
        }

        var blob = await client.DownloadAsync(acc, target.BlobId, "script.sieve", "application/sieve", ct).ConfigureAwait(false);
        if (!blob.IsOk)
        {
            return Result<SieveScriptState>.Fail(blob.Error!);
        }

        using var stream = blob.Value!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Result<SieveScriptState>.Ok(state with { Text = await reader.ReadToEndAsync(ct).ConfigureAwait(false) });
    }

    /// <summary>Uploads the text, has the server validate it, and replaces (or creates) the active script; nothing is saved when it does not validate.</summary>
    public async Task<Result<Unit>> SaveAsync(string text, SieveScriptState current, CancellationToken ct)
    {
        var saved = await SaveWithStateAsync(text, current, ct).ConfigureAwait(false);
        return saved.IsOk ? Result<Unit>.Ok(default) : Result<Unit>.Fail(saved.Error!);
    }

    /// <summary>Saves and returns the server's new state, when supplied.</summary>
    public async Task<Result<string?>> SaveWithStateAsync(string text, SieveScriptState current, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrEmpty(current.State))
        {
            return Result<string?>.Fail(new ValidationError("The server did not provide a script state; reload before saving."));
        }
        if (client is not JmapClient http)
        {
            return Result<string?>.Fail(new ValidationError("This connection cannot upload."));
        }

        var acc = client.Session.AccountFor(Capability);
        var up = await http.UploadAsync(acc, Encoding.UTF8.GetBytes(text), "application/sieve", ct).ConfigureAwait(false);
        if (!up.IsOk)
        {
            return Result<string?>.Fail(up.Error!);
        }

        var blob = up.Value!.BlobId;
        var v = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("SieveScript/validate", new JsonObject { ["accountId"] = acc, ["blobId"] = blob }, "v")), ct).ConfigureAwait(false);
        if (!v.IsOk)
        {
            return Result<string?>.Fail(v.Error!);
        }

        if (v.Value!.MethodResponses[0][1] is JsonObject vo && vo["error"] is JsonObject err)
        {
            return FailState(err.Str("description") ?? "invalid script");
        }

        var target = current.Target;
        var set = new JsonObject { ["accountId"] = acc, ["ifInState"] = current.State };
        if (target is not null)
        {
            set["update"] = new JsonObject { [target.Id] = new JsonObject { ["blobId"] = blob } };
            set["onSuccessActivateScript"] = target.Id;
        }
        else
        {
            set["create"] = new JsonObject { ["n"] = new JsonObject { ["name"] = "barbican", ["blobId"] = blob } };
            set["onSuccessActivateScript"] = "#n";
        }

        var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call("SieveScript/set", set, "s")), ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return Result<string?>.Fail(r.Error!);
        }

        var res = r.Value!.MethodResponses[0][1];
        if (res is JsonObject { } o && (o["updated"] is JsonObject || o["created"] is JsonObject))
        {
            return Result<string?>.Ok(o.Str("newState"));
        }

        var failed = ((res?["notUpdated"] as JsonObject) ?? (res?["notCreated"] as JsonObject))?.Select(kv => kv.Value).FirstOrDefault();
        var why = failed?.Str("description");
        return FailState(why ?? "the server refused the script");
    }

    private static Result<string?> FailState(string why) => Result<string?>.Fail(new ValidationError("Not saved: " + why));

    private IReadOnlyList<string> Extensions(string accountId)
    {
        if (client.Session.CapabilityObject(accountId, Capability) is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("sieveExtensions", out var e) && e.ValueKind == JsonValueKind.Array)
        {
            return [.. e.EnumerateArray().Select(x => x.GetString()).OfType<string>()];
        }

        return [];
    }
}
