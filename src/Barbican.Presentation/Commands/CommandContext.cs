// Mirrors: M2 plan section 4.1 (CommandContext). Contract from stage F of M2; owner menus.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Commands;

/// <summary>What a command runs on: the surface, the target messages, the open mailbox and one argument (a folder id, a tag id, a colour).</summary>
public sealed record CommandContext(
    CommandSurface Surface,
    IReadOnlyList<MailTarget> Targets,
    MailboxRef? Mailbox,
    string? Argument,
    string? AccountKey);
