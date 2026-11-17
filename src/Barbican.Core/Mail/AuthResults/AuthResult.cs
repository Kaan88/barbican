// Mirrors: Rust auth.rs:9 AuthResult; RFC 8601 (Authentication-Results); Bulwark lib/email-headers.ts. Stub from F2; owner mailcore.

using Barbican.Core.Common;

// CA1822: instance members of a stub that no stream has implemented yet; each gains state when its stream lands.
#pragma warning disable CA1822

namespace Barbican.Core.Mail.AuthResults;

/// <summary>One result of an Authentication-Results header: method, result and its properties.</summary>
public sealed record AuthResult(string Method, string Result, IReadOnlyList<KeyValuePair<string, string>> Props)
{
    /// <summary>A property value by name (case-insensitive), or null.</summary>
    public string? Prop(string name) => throw new PortPendingException("AuthResult.Prop: mailcore stream");
}
