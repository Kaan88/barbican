// Mirrors: Stalwart x:ArchivedItem (id, subject, archivedAt); see ArchivedItems.cs. J-074.

namespace Barbican.Core.Mail.Archive;

/// <summary>One item of Stalwart's deleted-item archive.</summary>
public sealed record ArchivedItem(string Id, string Subject, DateTimeOffset? ArchivedAt);
