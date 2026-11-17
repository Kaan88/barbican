// Mirrors: Rust jmap.rs:288 call (one request, fails on the method error), :1311 set_error, :1293 method_error; RFC 8620 §3.6.2, §5.3.
// The reader's few direct calls (receipts, Save to Files, the full body of a truncated message, CalendarEvent/parse, the write tests) go
// through this one helper so each failure becomes the same user message.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.Reader;

/// <summary>One-call JMAP helpers for the reader's own requests.</summary>
public static class ReaderCalls
{
    /// <summary>Sends one method call and returns its arguments, or the method error.</summary>
    public static async Task<Result<JsonObject>> CallAsync(IJmapClient client, string method, JsonObject args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(args);
        var sent = await client.SendAsync(JmapRequest.Of(JmapRequest.Call(method, args, "r")), ct).ConfigureAwait(false);
        return sent.IsOk ? sent.Value!.Args("r", method) : Result<JsonObject>.Fail(sent.Error!);
    }

    /// <summary>The id the server created for <paramref name="key"/> in a /set response, or the SetError in words.</summary>
    public static Result<string> Created(JsonObject response, string key)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(key);
        if (response["created"] is JsonObject created && created[key].Str("id") is { } id)
        {
            return Result<string>.Ok(id);
        }

        return Result<string>.Fail(response["notCreated"] is JsonObject failed && failed[key] is { } e
            ? SetErrors.From(key, e)
            : new ValidationError("The server created nothing."));
    }

    /// <summary>The ids of <paramref name="args"/>'s "ids" array (an Email/query or FileNode/query answer).</summary>
    public static IReadOnlyList<string> Ids(JsonObject args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return [.. (args["ids"] as JsonArray ?? []).Select(x => x.AsString()).OfType<string>()];
    }
}
