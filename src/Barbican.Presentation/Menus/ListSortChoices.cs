// Mirrors: Bulwark components/email/mail-list-menus.tsx:14-81 (SORT_CHOICES, sortChoiceFor, applySortChoice: the orders the list header offers; Settings has the full editor), lib/message-list-order.ts
// (isDefaultOrder, a trailing newest-first level is implicit). ML-09.

using Barbican.Core.Mail.Lists;

namespace Barbican.Presentation.Menus;

/// <summary>The orders the list's folder-actions menu offers, and how a stored order maps to one of them.</summary>
public static class ListSortChoices
{
    /// <summary>The default order, newest first: an empty list of levels.</summary>
    public const string NewestKey = "newest";

    /// <summary>One entry of the Sort submenu.</summary>
    public sealed record Choice(string Key, string Label, IReadOnlyList<MessageSortLevel> Levels);

    /// <summary>The choices in menu order (locales email_list.sort).</summary>
    public static IReadOnlyList<Choice> All { get; } =
    [
        new(NewestKey, "Newest first", []),
        new("oldest", "Oldest first", [new("receivedAt", "asc")]),
        new("unread", "Unread first", [new("unread", "desc")]),
        new("starred", "Starred first", [new("starred", "desc")]),
        new("sender", "Sender (A–Z)", [new("from", "asc")]),
        new("subject", "Subject (A–Z)", [new("subject", "asc")]),
        new("largest", "Largest first", [new("size", "desc")]),
    ];

    /// <summary>The label of the entry for an order only Settings can make.</summary>
    public const string CustomLabel = "Custom order (in Settings)";

    /// <summary>The levels of a choice, or null for an unknown key.</summary>
    public static IReadOnlyList<MessageSortLevel>? LevelsOf(string key) => All.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal))?.Levels;

    /// <summary>The choice that <paramref name="levels"/> is, or null for a custom order (sortChoiceFor).</summary>
    public static string? KeyFor(IReadOnlyList<MessageSortLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        // a trailing newest-first level is implicit
        var core = levels.Count > 1 && levels[^1] is { Criterion: "receivedAt", Direction: "desc" } ? levels.Take(levels.Count - 1).ToList() : [.. levels];
        if (core.Count == 0 || core is [{ Criterion: "receivedAt", Direction: "desc" }])
        {
            return NewestKey;
        }

        if (core.Count != 1)
        {
            return null;
        }

        return All.FirstOrDefault(c => c.Levels.Count == 1 && string.Equals(c.Levels[0].Criterion, core[0].Criterion, StringComparison.Ordinal)
            && string.Equals(c.Levels[0].Direction, core[0].Direction, StringComparison.Ordinal))?.Key;
    }
}
