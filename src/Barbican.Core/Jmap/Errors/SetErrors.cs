// Mirrors: Rust jmap.rs:1311 set_error, :1344 created_id; RFC 8620 §5.3 (SetError: notCreated/notUpdated/notDestroyed),
// RFC 8621 §2.3, §4.6, §7.5 (mail and submission set errors); Bulwark lib/jmap/jmap-error.ts:40 jmapSetError. Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Errors;

/// <summary>Per-object <c>/set</c> failures in words, and the result maps of a <c>/set</c> response.</summary>
public static class SetErrors
{
    private static readonly string[] FailureMaps = ["notCreated", "notUpdated", "notDestroyed"];

    /// <summary>The user message for a SetError.</summary>
    public static string Message(string type, string? description, IReadOnlyList<string>? properties)
    {
        ArgumentNullException.ThrowIfNull(type);
        var d = description ?? "";
        var withDetail = d.Length == 0 ? "" : $": {d}";
        return type switch
        {
            "overQuota" => "Your mailbox is full.",
            "tooLarge" => "The message is too large.",
            "rateLimit" => "Too many messages sent recently; try again later.",
            "forbiddenFrom" => "You may not send from that address.",
            "forbiddenMailFrom" => "The server refused the sender address.",
            "forbiddenToSend" => "This account may not send mail.",
            "invalidEmail" => $"The message is invalid{withDetail}.",
            "tooManyRecipients" => "Too many recipients.",
            "noRecipients" => "Add at least one recipient.",
            "invalidRecipients" => $"Some recipients are invalid{withDetail}.",
            "cannotUnsend" => "The message has already been sent.",
            "mailboxHasChild" => "Delete or move its subfolders first.",
            "mailboxHasEmail" => "The folder is not empty.",
            "tooManyKeywords" => "The message has too many tags.",
            "tooManyMailboxes" => "The message is in too many folders.",
            "blobNotFound" => "An attachment is no longer on the server; attach it again.",
            "notFound" => "It no longer exists.",
            "forbidden" => "You do not have permission to do that.",
            "singleton" => "This object can't be created or deleted.",
            "alreadyExists" => "That already exists.",
            "willDestroy" => "It is being deleted.",
            "invalidPatch" => $"The change is invalid{withDetail}.",
            "invalidProperties" => $"Invalid {string.Join(", ", properties ?? [])}{withDetail}",
            _ => d.Length == 0 ? type : $"{type}: {d}",
        };
    }

    /// <summary>A SetError object for id <paramref name="id"/> as the typed error.</summary>
    public static SetError From(string id, JsonNode? error)
    {
        ArgumentNullException.ThrowIfNull(id);
        var type = error.Str("type") ?? "error";
        var description = error.Str("description");
        var properties = error.Arr("properties") is null ? null : error.Strings("properties");
        return new SetError(id, type, description, properties) { Message = $"{id}: {Message(type, description, properties)}" };
    }

    /// <summary>
    /// The outcome of one <c>/set</c> response (RFC 8620 §5.3): created ids by creation id, updated and destroyed ids, and a
    /// <see cref="SetError"/> for every object in <c>notCreated</c>, <c>notUpdated</c> and <c>notDestroyed</c>.
    /// </summary>
    public static SetOutcome Read(JsonNode? response)
    {
        var created = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (cid, obj) in response.Obj("created") ?? [])
        {
            if (obj.Str("id") is { } id)
            {
                created[cid] = id;
            }
        }

        var updated = response.Obj("updated")?.Select(kv => kv.Key).ToList() ?? [];
        var failed = new List<SetError>();
        foreach (var key in FailureMaps)
        {
            foreach (var (id, err) in response.Obj(key) ?? [])
            {
                failed.Add(From(id, err));
            }
        }

        return new SetOutcome(created, updated, response.Strings("destroyed"), failed);
    }
}
