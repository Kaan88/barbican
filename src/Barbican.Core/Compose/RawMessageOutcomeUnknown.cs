using Barbican.Core.Jmap.Errors;
namespace Barbican.Core.Compose;

/// <summary>The server may have accepted a raw submission; callers must not automatically retry.</summary>
public sealed record RawMessageOutcomeUnknown() : JmapError("The server may have accepted the message. Verify the submission before retrying.");
