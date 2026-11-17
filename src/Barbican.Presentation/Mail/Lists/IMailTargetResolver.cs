// Mirrors: M2 plan section 4.4 (IMailTargetResolver: unified row ids to the real account and id). Contract from stage F of M2; owner unified (wave 2).

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Mail.Lists;

/// <summary>Turns a list row into the message an action applies to.</summary>
public interface IMailTargetResolver
{
    /// <summary>The target for a row, or null when the row has no message (a group header).</summary>
    MailTarget? Resolve(MessageRowViewModel row);
}
