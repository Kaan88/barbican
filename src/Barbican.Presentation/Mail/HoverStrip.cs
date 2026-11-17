// Mirrors: Bulwark components/email/email-hover-actions.tsx:150-290 (the buttons come from the hoverActions setting, default delete, star, markRead, archive; markRead swaps its icon
// and title with the message state, archive becomes "Move to Inbox" inside Archive, spam becomes "Not spam" in Junk, tag opens the picker), stores/settings-store.ts:1248
// (hoverActions default). Spec I-131..I-136. Owner actions (m2-close-a).

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Commands;

namespace Barbican.Presentation.Mail;

/// <summary>Builds the hover strip of a row from the setting and the state of the message.</summary>
public static class HoverStrip
{
    /// <summary>The prefs key of the configured actions (an array of action ids).</summary>
    public const string SettingKey = "hoverActions";

    /// <summary>The default set.</summary>
    public static IReadOnlyList<string> Defaults { get; } = ["delete", "star", "markRead", "archive"];

    /// <summary>The configured action ids; the default set when the setting is absent or names no known id.</summary>
    public static IReadOnlyList<string> Configured(JsonNode? stored)
    {
        if (stored is not JsonArray array)
        {
            return Defaults;
        }

        var ids = array.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
            .OfType<string>()
            .Where(id => id is "delete" or "star" or "markRead" or "archive" or "tag" or "spam")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return ids.Count == 0 && array.Count > 0 ? Defaults : ids;
    }

    /// <summary>The buttons for one row.</summary>
    /// <param name="unread">The message is unread.</param>
    /// <param name="flagged">The message is starred.</param>
    /// <param name="role">The role of the folder on screen.</param>
    /// <param name="archiveMovesToInbox">The folder on screen is the Archive or below it, and an Inbox exists.</param>
    /// <param name="actions">The configured action ids.</param>
    public static IReadOnlyList<HoverButton> For(bool unread, bool flagged, MailboxRole role, bool archiveMovesToInbox, IReadOnlyList<string> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var buttons = new List<HoverButton>();
        foreach (var id in actions)
        {
            switch (id)
            {
                case "delete":
                    buttons.Add(new HoverButton(id, CommandIds.Delete, "Trash", "Delete"));
                    break;
                case "star":
                    buttons.Add(new HoverButton(id, CommandIds.ToggleStar, "Star", flagged ? "Unstar" : "Star", flagged));
                    break;
                case "markRead":
                    // the icon shows the action: a check for "Mark as read", an envelope for "Mark as unread" (Bulwark MailOpen / Mail)
                    buttons.Add(new HoverButton(id, CommandIds.ToggleRead, unread ? "Check" : "Mail", unread ? "Mark as read" : "Mark as unread"));
                    break;
                case "archive":
                    buttons.Add(archiveMovesToInbox
                        ? new HoverButton(id, CommandIds.MoveToInbox, "Inbox", "Move to Inbox")
                        : new HoverButton(id, CommandIds.Archive, "Archive", "Archive"));
                    break;
                case "tag":
                    buttons.Add(new HoverButton(id, CommandIds.Tag, "Tag", "Tag"));
                    break;
                case "spam" when role is not (MailboxRole.Sent or MailboxRole.Drafts):
                    buttons.Add(role == MailboxRole.Junk
                        ? new HoverButton(id, CommandIds.NotSpam, "Shield", "Not spam")
                        : new HoverButton(id, CommandIds.ReportSpam, "Alert", "Report spam"));
                    break;
            }
        }

        return buttons;
    }
}
