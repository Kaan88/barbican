// Mirrors: RFC 8621 section 2.4 (Mailbox myRights); Bulwark lib/jmap/types.ts MailboxRights and its use in components/layout/mailbox-context-menu.tsx
// (a folder the user may not change offers no write rows); spec J-048. Owner menus.

using System.Text.Json;
using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Menus;

/// <summary>What the user may do in one mailbox; every right defaults to true (a server that sends none, or an own mailbox).</summary>
public sealed record MailboxRights(
    bool MayReadItems = true,
    bool MayAddItems = true,
    bool MayRemoveItems = true,
    bool MaySetSeen = true,
    bool MaySetKeywords = true,
    bool MayCreateChild = true,
    bool MayRename = true,
    bool MayDelete = true,
    bool MaySubmit = true)
{
    /// <summary>Every right.</summary>
    public static MailboxRights Full { get; } = new();

    /// <summary>Reads <c>myRights</c> from the properties the model keeps; a missing right counts as granted.</summary>
    public static MailboxRights From(Mailbox mailbox)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        if (mailbox.Extra is null || !mailbox.Extra.TryGetValue("myRights", out var rights) || rights.ValueKind != JsonValueKind.Object)
        {
            return Full;
        }

        bool Right(string name) => !rights.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.False;
        return new MailboxRights(
            Right("mayReadItems"),
            Right("mayAddItems"),
            Right("mayRemoveItems"),
            Right("maySetSeen"),
            Right("maySetKeywords"),
            Right("mayCreateChild"),
            Right("mayRename"),
            Right("mayDelete"),
            Right("maySubmit"));
    }
}
