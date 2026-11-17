// Mirrors: RFC 8621 section 7.5 (EmailSubmission/set: create with emailId, identityId and an explicit envelope); Stalwart
// crates/jmap/src/submission/set.rs (the email and the identity must exist; created carries id and undoStatus). J-068: the receipt path of the
// reader (MdnSender). Test convention: a recipient at refused.example (RFC 2606 name) is refused with invalidRecipients, so the
// "destroy the imported copy on failure" path can be driven. Owner reader.

using System.Globalization;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private readonly List<JsonObject> _submissions = [];

    /// <summary>Every accepted submission (create object as sent), in order.</summary>
    public IReadOnlyList<JsonObject> Submissions
    {
        get
        {
            lock (_lock)
            {
                return [.. _submissions.Select(s => (JsonObject)s.DeepClone())];
            }
        }
    }

    private (string? Name, JsonObject Args) EmailSubmissionSet(JsonObject args)
    {
        var created = new JsonObject();
        var notCreated = new JsonObject();
        foreach (var (key, value) in args["create"] as JsonObject ?? [])
        {
            if (SubmissionError(value as JsonObject) is { } error)
            {
                notCreated[key] = error;
                continue;
            }

            lock (_lock)
            {
                _submissions.Add((JsonObject)value!.DeepClone());
                created[key] = new JsonObject { ["id"] = "S" + _submissions.Count.ToString(CultureInfo.InvariantCulture), ["undoStatus"] = "final" };
            }
        }

        return (null, new JsonObject
        {
            ["accountId"] = options.AccountId,
            ["oldState"] = "s0",
            ["newState"] = "s1",
            ["created"] = created,
            ["notCreated"] = notCreated,
        });
    }

    private JsonObject? SubmissionError(JsonObject? create)
    {
        var emailId = (string?)create?["emailId"];
        var rcpts = (create?["envelope"]?["rcptTo"] as JsonArray ?? []).Select(r => (string?)r?["email"]).ToList();
        if (emailId is null || store.Email(emailId) is null)
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("emailId") };
        }

        if (!string.Equals((string?)create!["identityId"], "i1", StringComparison.Ordinal))
        {
            return new JsonObject { ["type"] = "invalidProperties", ["properties"] = new JsonArray("identityId") };
        }

        if (rcpts.Count == 0 || rcpts.Any(r => r is null || r.EndsWith("@refused.example", StringComparison.OrdinalIgnoreCase)))
        {
            return new JsonObject { ["type"] = "invalidRecipients", ["invalidRecipients"] = new JsonArray([.. rcpts.Select(r => (JsonNode?)r)]) };
        }

        return null;
    }
}
