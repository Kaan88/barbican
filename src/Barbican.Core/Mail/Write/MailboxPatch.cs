// Mirrors: RFC 8621 section 2.5 (Mailbox/set update: name, parentId, sortOrder, isSubscribed); Bulwark lib/jmap/client.ts updateMailbox :4686. Contract from stage F of M2; owner actions.

namespace Barbican.Core.Mail.Write;

/// <summary>The properties of a mailbox to change; null means unchanged (ParentId has its own flag because null is a valid parent: the top level).</summary>
public sealed record MailboxPatch
{
    public string? Name { get; init; }

    public bool SetParent { get; init; }

    public string? ParentId { get; init; }

    public int? SortOrder { get; init; }

    public bool? IsSubscribed { get; init; }
}
