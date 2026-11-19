// Mirrors: Bulwark lib/message-list-order.ts:95-220,333-375; RFC 8621 section 4.4.2.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Lists;

/// <summary>Validates persisted ordering and maps it to server comparators, including a stable date tie-breaker.</summary>
public static class MessageListOrder
{
    private static readonly HashSet<string> Criteria = new(["unread", "starred", "tag", "receivedAt", "sentAt", "from", "subject", "size"], StringComparer.Ordinal);

    public static IReadOnlyList<MessageSortLevel> Parse(JsonNode? value)
    {
        var levels = new List<MessageSortLevel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in (value as JsonArray ?? []).OfType<JsonObject>())
        {
            var criterion = Text(item["criterion"]);
            if (criterion is null || !Criteria.Contains(criterion)) continue;
            var direction = Text(item["direction"]);
            if (direction is not ("asc" or "desc")) direction = criterion is "from" or "subject" ? "asc" : "desc";
            var tag = string.Equals(criterion, "tag", StringComparison.Ordinal) ? Text(item["tagId"]) : null;
            if (string.Equals(criterion, "tag", StringComparison.Ordinal) && string.IsNullOrEmpty(tag)) continue;
            if (!seen.Add(string.Equals(criterion, "tag", StringComparison.Ordinal) ? "tag:" + tag : criterion)) continue;
            levels.Add(new MessageSortLevel(criterion, direction, tag));
            if (levels.Count == 3) break;
        }

        return levels;
    }

    public static IReadOnlyList<MessageSortLevel> Preset(string preset, string? tagId = null) => preset switch
    {
        "unread_first" => [new("unread", "desc")],
        "starred_first" => [new("starred", "desc")],
        "tagged_first" when !string.IsNullOrEmpty(tagId) => [new("tag", "desc", tagId)],
        _ => [],
    };

    public static JsonArray ToJson(IReadOnlyList<MessageSortLevel> levels) => new([.. levels.Select(level => (JsonNode)new JsonObject
    {
        ["criterion"] = level.Criterion, ["direction"] = level.Direction, ["tagId"] = level.TagId,
    })]);

    public static IReadOnlyList<SortComparator> Build(IReadOnlyList<MessageSortLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        var result = new List<SortComparator>();
        foreach (var level in Parse(ToJson(levels)))
        {
            var keyword = level.Criterion switch { "unread" => "$seen", "starred" => "$flagged", "tag" => "$label:" + level.TagId, _ => null };
            // RFC: false < true. Unread is the absence of $seen; other booleans mean presence.
            var ascending = string.Equals(level.Direction, "asc", StringComparison.Ordinal);
            result.Add(keyword is null ? new(level.Criterion, ascending) : new("hasKeyword", string.Equals(level.Criterion, "unread", StringComparison.Ordinal) ? !ascending : ascending, keyword));
        }

        if (!result.Any(item => string.Equals(item.Property, "receivedAt", StringComparison.Ordinal))) result.Add(new("receivedAt", false));
        return result;
    }

    public static JsonArray ComparatorsJson(IReadOnlyList<SortComparator>? sort) => new([.. (sort ?? [new("receivedAt", false)]).Select(item =>
    {
        var value = new JsonObject { ["property"] = item.Property, ["isAscending"] = item.IsAscending };
        if (item.Keyword is not null) value["keyword"] = item.Keyword;
        return (JsonNode)value;
    })]);

    /// <summary>Semantic comparator for merging pages from separate accounts; text collation approximates server collation.</summary>
    public static int Compare(EmailSummary left, EmailSummary right, IReadOnlyList<SortComparator>? sort)
    {
        foreach (var item in sort ?? [new("receivedAt", false)])
        {
            int difference;
            if (item.Property is "from" or "subject")
            {
                var a = item.Property is "from" ? Sender(left) : Subject(left);
                var b = item.Property is "from" ? Sender(right) : Subject(right);
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                {
                    if (string.IsNullOrEmpty(a) != string.IsNullOrEmpty(b)) return string.IsNullOrEmpty(a) ? 1 : -1;
                    continue;
                }

                difference = StringComparer.CurrentCultureIgnoreCase.Compare(a, b);
            }
            else difference = item.Property switch
            {
                "hasKeyword" => left.Keywords.IsSet(item.Keyword ?? "").CompareTo(right.Keywords.IsSet(item.Keyword ?? "")),
                "size" => left.Size.CompareTo(right.Size),
                "sentAt" => (left.SentAt ?? left.ReceivedAt ?? DateTimeOffset.UnixEpoch).CompareTo(right.SentAt ?? right.ReceivedAt ?? DateTimeOffset.UnixEpoch),
                _ => (left.ReceivedAt ?? DateTimeOffset.UnixEpoch).CompareTo(right.ReceivedAt ?? DateTimeOffset.UnixEpoch),
            };
            if (difference != 0) return item.IsAscending ? Math.Sign(difference) : -Math.Sign(difference);
        }

        return 0;
    }

    private static string Sender(EmailSummary email) => email.From.Count == 0 ? "" : string.IsNullOrEmpty(email.From[0].Name) ? email.From[0].Email : email.From[0].Name!;

    private static string Subject(EmailSummary email) => Regex.Replace(email.Subject ?? "", @"^\s*(?:(?:re|fwd?|aw|wg|sv|vs|tr)\s*(?:\[\d+\])?\s*:\s*)+", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)).Trim();

    private static string? Text(JsonNode? value) => value is JsonValue v && v.TryGetValue<string>(out var text) ? text : null;
}
