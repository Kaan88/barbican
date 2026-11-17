// A message to put into the fake store (synthetic, example.org only). Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>A synthetic message.</summary>
public sealed record FakeMail
{
    public string Subject { get; init; } = "";

    public string FromName { get; init; } = "Sender";

    public string FromEmail { get; init; } = "sender@example.org";

    public string To { get; init; } = "user@example.org";

    public DateTimeOffset ReceivedAt { get; init; } = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    public string Text { get; init; } = "";

    public string? Html { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public IReadOnlyList<FakeAttachment> Attachments { get; init; } = [];

    /// <summary>An existing message whose conversation this one joins (a reply).</summary>
    public string? ReplyTo { get; init; }
}
