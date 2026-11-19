// Mirrors: Bulwark lib/message-list-order.ts:11-35, RFC 8621 section 4.4.2.
namespace Barbican.Core.Mail.Lists;

/// <summary>One semantic message order level; direction is asc or desc.</summary>
public sealed record MessageSortLevel(string Criterion, string Direction, string? TagId = null);
