// Builders for test messages. Owner mailcore.

using Barbican.Core.Mail.Model;

namespace Barbican.Presentation.Tests.Support;

internal static class Mails
{
    public static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public static EmailSummary Make(string id, bool unread = false, double hoursAgo = 1, string from = "Ada Lovelace", string? subject = null, int threadCount = 1) =>
        new()
        {
            Id = id,
            ThreadId = "t" + id,
            Subject = subject ?? "Subject " + id,
            Preview = "Preview of " + id,
            ReceivedAt = Now.AddHours(-hoursAgo),
            From = [new EmailAddress(from, from.Replace(' ', '.').ToLowerInvariant() + "@a.example.com")],
            Keywords = unread ? Keywords.None : Keywords.Of("$seen"),
            ThreadCount = threadCount,
        };

    /// <summary>A page of <paramref name="count"/> messages named m{start}.., newest first, one hour apart.</summary>
    public static ListPage Page(int start, int count, int total, bool more) =>
        new()
        {
            Items = [.. Enumerable.Range(start, count).Select(i => Make("m" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), hoursAgo: i * 0.5))],
            Total = total,
            Next = more ? new ListCursor("m" + (start + count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)) : null,
        };
}
