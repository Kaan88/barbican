// Mirrors: RFC 8621 section 4.4.2 (Email/query sort: property, isAscending); Bulwark lib/jmap/search-utils.ts. Contract from stage F of M2; owner search (wave 2).

namespace Barbican.Core.Mail.Model;

/// <summary>One sort key of Email/query.</summary>
public sealed record SortComparator(string Property, bool IsAscending, string? Keyword = null);
