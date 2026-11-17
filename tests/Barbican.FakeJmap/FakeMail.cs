// A message to put into the fake store (synthetic, example.org only). Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>A synthetic message.</summary>
public sealed record FakeMail
{
    public string Subject { get; init; } = "";

    public string FromName { get; init; } = "Sender";

    public string FromEmail { get; init; } = "sender@example.org";

    public string To { get; init; } = "user@example.org";

    /// <summary>More To recipients after <see cref="To"/> (M3 reply-all tests).</summary>
    public IReadOnlyList<string> MoreTo { get; init; } = [];

    /// <summary>Cc recipients (M3 reply-all tests).</summary>
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>The Reply-To address, if any (M3 reply tests).</summary>
    public string? ReplyToEmail { get; init; }

    public DateTimeOffset ReceivedAt { get; init; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public string Text { get; init; } = "";

    public string? Html { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<FakeAttachment> Attachments { get; init; } = [];

    /// <summary>Extra header fields (name, value) in order, for example List-Unsubscribe or Disposition-Notification-To (M2 seed).</summary>
    public IReadOnlyList<(string Name, string Value)> Headers { get; init; } = [];

    /// <summary>The Authentication-Results value the server wrote for this message; null: the default pass under mx.example.org (M2 seed).</summary>
    public string? AuthenticationResults { get; init; }

    /// <summary>The Message-ID (without angle brackets); null: the store makes one from the id. Email/import keeps the blob's own.</summary>
    public string? MessageId { get; init; }

    /// <summary>An existing message whose conversation this one joins (a reply).</summary>
    public string? ReplyTo { get; init; }
}
