// Mirrors: Rust jmap.rs:1619-:1633 (valid_keyword, keyword_path, pct); RFC 8621 §4.1.1; Bulwark lib/jmap/patch-pointer.ts,
// server-keywords.ts. Stub from F2; owner jmap.

using Barbican.Core.Common;

namespace Barbican.Core.Jmap.KeywordSyntax;

/// <summary>Keyword syntax and patch paths.</summary>
public static class KeywordRules
{
    /// <summary>RFC 8621 keyword syntax (printable ASCII without the forbidden characters).</summary>
    public static bool IsValid(string keyword) => throw new PortPendingException("KeywordRules.IsValid: jmap stream");

    /// <summary>The JSON Pointer patch path of a keyword (lower-cased, <c>~</c> and <c>/</c> escaped).</summary>
    public static string Path(string keyword) => throw new PortPendingException("KeywordRules.Path: jmap stream");

    /// <summary>Percent-encodes a URL path or query component (UTF-8).</summary>
    public static string PercentEncode(string text) => throw new PortPendingException("KeywordRules.PercentEncode: jmap stream");
}
