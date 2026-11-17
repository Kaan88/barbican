// Mirrors: Rust dnd.rs:42 already_in, :149 plan_folder_drop; Bulwark lib/folder-tree-dnd.ts:41-134 (reorder and reparent rules);
// RFC 8621 §2.5 (Mailbox/set: parentId, sortOrder). Stalwart crates/jmap/src/mailbox/set.rs (parentId cycles are refused server side too).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.DragDrop;

/// <summary>The pure decisions behind dropping mail or a folder onto a folder.</summary>
public static class FolderDrop
{
    /// <summary>Whether the message's only folder is already <paramref name="folderId"/> (a move there changes nothing).</summary>
    public static bool AlreadyIn(JsonNode message, string folderId)
    {
        ArgumentNullException.ThrowIfNull(message);
        return message["mailboxIds"] is JsonObject o && o.Count == 1 && o.ContainsKey(folderId);
    }

    /// <summary>
    /// Mailbox/set patches for dropping folder <paramref name="drag"/> onto <paramref name="target"/>: Inside nests it (last), Before and
    /// After put it next to the target under the target's parent. Only what changes is written; role folders keep their sortOrder.
    /// A cycle, an unknown folder or moving a role folder to another parent is refused with a user message.
    /// </summary>
    public static Result<IReadOnlyList<FolderPatch>> Plan(IReadOnlyList<JsonObject> folders, string drag, string target, DropZone zone)
    {
        ArgumentNullException.ThrowIfNull(folders);
        var byId = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var m in folders)
        {
            byId[IdOf(m)] = m;
        }

        if (!byId.TryGetValue(drag, out var d))
        {
            return Refuse("That folder is gone");
        }

        if (!byId.TryGetValue(target, out var t))
        {
            return Refuse("Can't drop it there");
        }

        if (string.Equals(drag, target, StringComparison.Ordinal))
        {
            return Result<IReadOnlyList<FolderPatch>>.Ok([]);
        }

        var newParent = zone == DropZone.Inside ? target : ParentOf(t);
        if (string.Equals(newParent, drag, StringComparison.Ordinal) || Descendants(folders, drag).Contains(newParent))
        {
            return Refuse("A folder can't go inside itself");
        }

        if (RoleOf(d).Length > 0 && !string.Equals(newParent, ParentOf(d), StringComparison.Ordinal))
        {
            return Refuse("Special folders (Inbox, Sent, …) stay where they are");
        }

        var order = Siblings(folders, newParent).Where(x => !string.Equals(x, drag, StringComparison.Ordinal)).ToList();
        if (zone == DropZone.Inside)
        {
            order.Add(drag);
        }
        else
        {
            var at = order.FindIndex(x => string.Equals(x, target, StringComparison.Ordinal));
            if (at < 0)
            {
                at = order.Count;
            }

            order.Insert(zone == DropZone.After ? at + 1 : at, drag);
        }

        return Result<IReadOnlyList<FolderPatch>>.Ok(Patches(order, byId, drag, newParent));
    }

    private static List<FolderPatch> Patches(List<string> order, Dictionary<string, JsonObject> byId, string drag, string newParent)
    {
        var patches = new List<FolderPatch>();
        for (var i = 0; i < order.Count; i++)
        {
            var id = order[i];
            var m = byId[id];
            var p = new JsonObject();
            var so = i + 1;

            // role folders sort by role first, so their sortOrder is never rewritten (Rust dnd.rs:171)
            if (RoleOf(m).Length == 0 && SortOf(m) != so)
            {
                p["sortOrder"] = so;
            }

            if (string.Equals(id, drag, StringComparison.Ordinal) && !string.Equals(ParentOf(m), newParent, StringComparison.Ordinal))
            {
                p["parentId"] = newParent.Length == 0 ? null : newParent;
            }

            if (p.Count > 0)
            {
                patches.Add(new FolderPatch(id, p));
            }
        }

        return patches;
    }

    private static Result<IReadOnlyList<FolderPatch>> Refuse(string text) =>
        Result<IReadOnlyList<FolderPatch>>.Fail(new ValidationError(text));

    private static string IdOf(JsonObject m) => m["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static string ParentOf(JsonObject m) => m["parentId"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static string RoleOf(JsonObject m) => m["role"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static long SortOf(JsonObject m) => m["sortOrder"] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;

    private static int RoleRank(string role) => role switch
    {
        "inbox" => 0,
        "drafts" => 1,
        "sent" => 2,
        "archive" => 3,
        "junk" => 4,
        "trash" => 5,
        _ => 10,
    };

    /// <summary>Children of a parent in the order the sidebar shows them (role folders first, then sortOrder, name).</summary>
    private static List<string> Siblings(IReadOnlyList<JsonObject> list, string parent) =>
        [.. list.Where(m => string.Equals(ParentOf(m), parent, StringComparison.Ordinal))
            .OrderBy(m => RoleRank(RoleOf(m)))
            .ThenBy(SortOf)
            .ThenBy(m => (m["name"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty).ToLowerInvariant(), StringComparer.Ordinal)
            .Select(IdOf)];

    private static HashSet<string> Descendants(IReadOnlyList<JsonObject> list, string root)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            foreach (var m in list.Where(m => string.Equals(ParentOf(m), p, StringComparison.Ordinal)))
            {
                var id = IdOf(m);
                if (result.Add(id))
                {
                    stack.Push(id);
                }
            }
        }

        return result;
    }
}
