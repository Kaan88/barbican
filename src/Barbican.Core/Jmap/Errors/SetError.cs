// Mirrors: RFC 8620 §5.3 (SetError) and RFC 8621 §7.5 (EmailSubmission set errors).

namespace Barbican.Core.Jmap.Errors;

/// <summary>A per-object failure in a <c>/set</c> response.</summary>
public sealed record SetError(string Id, string Type, string? Description, IReadOnlyList<string>? Properties)
    : JmapError($"{Id}: {Type}");
