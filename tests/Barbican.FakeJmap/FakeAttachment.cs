// An attachment of a synthetic message. Owner jmap.

namespace Barbican.FakeJmap;

/// <summary>An attached or inline part.</summary>
/// <param name="Name">File name.</param>
/// <param name="Type">MIME type.</param>
/// <param name="Content">The bytes.</param>
/// <param name="Cid">Content-ID for an inline part, else null.</param>
public sealed record FakeAttachment(string Name, string Type, byte[] Content, string? Cid = null);
