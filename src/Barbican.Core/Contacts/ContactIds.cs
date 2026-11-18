// Mirrors: Rust calendar.rs:226 ns, :231 split_ns, :341 namespace_event (the same scheme for contacts, D-M5-A01); B-CON-001. Another account's ids are prefixed "<account>~" so ids from several accounts never collide. Owner con-data.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Contacts;

/// <summary>Namespaced address book and contact ids.</summary>
public static class ContactIds
{
    /// <summary>The primary account's ids stay plain; a shared account's are "x~id".</summary>
    public static string Ns(string primaryAccount, string account, string id) =>
        string.Equals(account, primaryAccount, StringComparison.Ordinal) || string.IsNullOrEmpty(account) ? id : $"{account}~{id}";

    /// <summary>The account named in an id (null for a plain one) and the server id.</summary>
    public static (string? Account, string Id) SplitNs(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var i = id.IndexOf('~', StringComparison.Ordinal);
        return i < 0 ? (null, id) : (id[..i], id[(i + 1)..]);
    }

    /// <summary>Gives a book or card of another account namespaced ids (its own and, for a card, its books'); <c>_rid</c> keeps the server id and <c>_acc</c> the account.</summary>
    public static void Namespace(JsonNode item, string primaryAccount, string account)
    {
        if (item is not JsonObject o)
        {
            return;
        }

        var raw = o.Str("id") ?? string.Empty;
        o["_rid"] = raw;
        o["_acc"] = account;
        o["id"] = Ns(primaryAccount, account, raw);
        if (!string.Equals(account, primaryAccount, StringComparison.Ordinal))
        {
            o["_shared"] = true;
            if (o["addressBookIds"] is JsonObject ids)
            {
                var m = new JsonObject();
                foreach (var (k, v) in ids)
                {
                    m[Ns(primaryAccount, account, k)] = v?.DeepClone();
                }

                o["addressBookIds"] = m;
            }
        }
    }

    /// <summary>The addressBookIds map with the namespace removed, for the server.</summary>
    public static JsonObject ServerBookIds(JsonNode? bookIds)
    {
        var m = new JsonObject();
        if (bookIds is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                m[SplitNs(k).Id] = v?.DeepClone();
            }
        }

        return m;
    }
}
