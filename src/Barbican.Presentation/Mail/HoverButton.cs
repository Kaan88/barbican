// Mirrors: Bulwark components/email/email-hover-actions.tsx:51-110 (ACTION_CONFIG: the icon and title of each hover action) and :240-275 (the icon and title that change with the message state).

namespace Barbican.Presentation.Mail;

/// <summary>One button of a row hover strip.</summary>
/// <param name="Id">The action id: delete, star, markRead, archive, tag or spam (Bulwark HoverAction).</param>
/// <param name="CommandId">The command it runs on the message of the row.</param>
/// <param name="Icon">A key of the icon set.</param>
/// <param name="Tooltip">The title: the words the row menu uses for the same action (Bulwark R10).</param>
/// <param name="Active">Shows the on state (the star of a starred message is filled).</param>
public sealed record HoverButton(string Id, string CommandId, string Icon, string Tooltip, bool Active = false)
{
    /// <summary>The tag button opens the tag picker (the Tag flyout of the row menu) instead of running a command.</summary>
    public bool OpensTagPicker => string.Equals(Id, "tag", StringComparison.Ordinal);
}
