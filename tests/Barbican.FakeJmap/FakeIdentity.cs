// An identity of the fake account (M3 send pipeline): RFC 8621 section 6 (Identity: name, email, replyTo, textSignature, htmlSignature). Owner send-pipeline.

namespace Barbican.FakeJmap;

/// <summary>A sender identity the fake server lists.</summary>
public sealed record FakeIdentity(string Id, string Name, string Email)
{
    public string TextSignature { get; init; } = "";

    public string HtmlSignature { get; init; } = "";

    public string? ReplyTo { get; init; }
}
