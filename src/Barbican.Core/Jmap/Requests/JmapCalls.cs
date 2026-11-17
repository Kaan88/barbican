// Mirrors: Rust jmap.rs:288 call (fail on the first method error), :352 query_all (page by position until total: a server may cap
// `limit`, RFC 8620 §5.5), :371 get_all (chunks of maxObjectsInGet, several chunks per request by maxCallsInRequest, RFC 8620 §5.1);
// Bulwark lib/jmap/request-limits.ts:14 batched. Owner jmap.

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Requests;

/// <summary>Request helpers over <see cref="IJmapClient"/>.</summary>
public static class JmapCalls
{
    private const int QueryPage = 5000;
    private const int QueryCeiling = 1_000_000;

    /// <summary>Sends the request and fails on the first method error (named after the method that was called).</summary>
    public static async Task<Result<JmapResponse>> CallAsync(this IJmapClient client, JmapRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        var r = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (!r.IsOk)
        {
            return r;
        }

        var methods = request.MethodCalls.Where(c => c.Count > 2).ToDictionary(c => c[2].AsString() ?? "", c => c[0].AsString() ?? "", StringComparer.Ordinal);
        var error = r.Value!.FirstError(id => methods.TryGetValue(id, out var m) ? m : "?");
        return error is null ? r : Result<JmapResponse>.Fail(error);
    }

    /// <summary>Every id a <c>Foo/query</c> matches, paged by position until the total is reached.</summary>
    public static async Task<Result<IReadOnlyList<string>>> QueryAllAsync(this IJmapClient client, string method, string accountId, JsonObject args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(args);
        var ids = new List<string>();
        while (true)
        {
            var a = (JsonObject)args.DeepClone();
            a["accountId"] = accountId;
            a["position"] = ids.Count;
            a["calculateTotal"] = true;
            a["limit"] ??= QueryPage;
            var r = await client.CallAsync(JmapRequest.Of(JmapRequest.Call(method, a, "q")), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<IReadOnlyList<string>>.Fail(r.Error!);
            }

            var res = r.Value!.MethodResponses[0][1];
            var page = res.Strings("ids");
            if (page.Count == 0)
            {
                break;
            }

            ids.AddRange(page);
            var total = res is JsonObject o && o["total"].AsNumber() is { } t ? (long?)t : null;
            if (total is null || ids.Count >= total || ids.Count > QueryCeiling)
            {
                break;
            }
        }

        return Result<IReadOnlyList<string>>.Ok(ids);
    }

    /// <summary><c>Foo/get</c> of many ids: chunks of maxObjectsInGet, several chunks per request (maxCallsInRequest).</summary>
    public static async Task<Result<IReadOnlyList<JsonObject>>> GetAllAsync(
        this IJmapClient client, string method, string accountId, IReadOnlyList<string> ids, JsonArray? properties, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(ids);
        var output = new List<JsonObject>();
        var limits = client.Session.Limits;
        foreach (var group in ids.Chunk(limits.MaxObjectsInGet).Chunk(limits.MaxCallsInRequest))
        {
            var calls = group.Select((chunk, i) =>
            {
                var a = new JsonObject { ["accountId"] = accountId, ["ids"] = new JsonArray([.. chunk.Select(x => (JsonNode)x)]) };
                if (properties is not null)
                {
                    a["properties"] = properties.DeepClone();
                }

                return JmapRequest.Call(method, a, "g" + i.ToString(CultureInfo.InvariantCulture));
            }).ToArray();
            var r = await client.CallAsync(JmapRequest.Of(calls), ct).ConfigureAwait(false);
            if (!r.IsOk)
            {
                return Result<IReadOnlyList<JsonObject>>.Fail(r.Error!);
            }

            foreach (var resp in r.Value!.MethodResponses)
            {
                output.AddRange(resp[1].Arr("list")?.OfType<JsonObject>() ?? []);
            }
        }

        return Result<IReadOnlyList<JsonObject>>.Ok(output);
    }
}
