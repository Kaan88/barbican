// Mirrors: Rust jmap.rs:1619 pct, :1627 valid_keyword, :1633 keyword_path; RFC 8621 §4.1.1 (keyword syntax), RFC 8620 §5.3
// (PatchObject keys are JSON Pointers, RFC 6901), RFC 6570 level 1 (URI templates, RFC 8620 §6.2); Bulwark
// lib/jmap/patch-pointer.ts:15 pointerToken, :28 keywordPointer; lib/jmap/server-keywords.ts:14 (keywords are case-insensitive).
// Owner jmap.

using System.Globalization;
using System.Text;

namespace Barbican.Core.Jmap.KeywordSyntax;

/// <summary>Keyword syntax, patch paths and URI-template percent-encoding.</summary>
public static class KeywordRules
{
    private const string Forbidden = "(){]%*\"\\";

    /// <summary>RFC 8621 keyword syntax: 1-255 of %x21-7E without <c>( ) { ] % * " \</c>.</summary>
    public static bool IsValid(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        if (keyword.Length is 0 or > 255)
        {
            return false;
        }

        foreach (var c in keyword)
        {
            if (c < 0x21 || c > 0x7e || Forbidden.Contains(c, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The JSON Pointer patch path of a keyword (lower-cased, <c>~</c> as <c>~0</c> and <c>/</c> as <c>~1</c>).</summary>
    public static string Path(string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        return "keywords/" + PointerToken(keyword.ToLowerInvariant());
    }

    /// <summary>One JSON Pointer reference token: <c>~</c> to <c>~0</c>, then <c>/</c> to <c>~1</c> (RFC 6901 §3).</summary>
    public static string PointerToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    /// <summary>Percent-encodes a URI template value (UTF-8; only the RFC 3986 unreserved characters stay).</summary>
    public static string PercentEncode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            if (b is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9'
                or (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
            {
                sb.Append((char)b);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }
}
