// Mirrors: M2 plan section 4.3 (IMailSelection: the list's selection as targets; menus, palette and toolbar read it). Contract from stage F of M2; owner actions.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Actions;

/// <summary>The selection of the message list as targets.</summary>
public interface IMailSelection
{
    IReadOnlyList<MailTarget> Current { get; }

    event EventHandler? Changed;
}
