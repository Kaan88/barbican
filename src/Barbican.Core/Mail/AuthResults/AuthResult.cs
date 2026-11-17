// Mirrors: Rust auth.rs:9 AuthResult; RFC 8601 (Authentication-Results); Bulwark lib/email-headers.ts. Owner mailcore.

namespace Barbican.Core.Mail.AuthResults;

/// <summary>One result of an Authentication-Results header: method, result and its properties.</summary>
public sealed record AuthResult(string Method, string Result, IReadOnlyList<KeyValuePair<string, string>> Props)
{
    /// <summary>A property value by name (case-insensitive), or null.</summary>
    public string? Prop(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (var (key, value) in Props)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}
