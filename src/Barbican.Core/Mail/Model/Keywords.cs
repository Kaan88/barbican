// Mirrors: Bulwark lib/jmap/types.ts, RFC 8621; Rust jmap.rs / mail.rs value shapes (JSON there, typed here). Model stub from F2 (M1-plan §4.1); owner jmap.

namespace Barbican.Core.Mail.Model;

/// <summary>The keywords of a message (RFC 8621 §4.1.1): <c>$seen</c>, <c>$flagged</c>, <c>$label:x</c>.</summary>
public sealed record Keywords(IReadOnlyDictionary<string, bool> Map)
{
    /// <summary>No keywords set.</summary>
    public static Keywords None { get; } = new(new Dictionary<string, bool>(StringComparer.Ordinal));

    /// <summary>The keywords of the given names, all set.</summary>
    public static Keywords Of(params string[] set) => new(set.ToDictionary(k => k, _ => true, StringComparer.Ordinal));

    /// <summary>True when the keyword is present and true.</summary>
    public bool IsSet(string keyword) => Map.TryGetValue(keyword, out var on) && on;

    /// <summary>The read flag.</summary>
    public bool Seen => IsSet("$seen");

    /// <summary>The star flag.</summary>
    public bool Flagged => IsSet("$flagged");
}
