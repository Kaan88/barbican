// Mirrors: Rust ai.rs:189-191 (the 30 000 character clip), :197-222 (the <email> tag); B-AI-004. Mail text that goes to an add-on's model is data, never instructions.

using System.Text.RegularExpressions;

namespace Barbican.Core.Plugins.Native.Host;

/// <summary>Wraps mail text for an add-on (the injection guard of the host tools).</summary>
public static partial class UntrustedMailText
{
    /// <summary>The longest mail text handed over.</summary>
    public const int ClipLength = 30_000;

    private const string Truncated = " [truncated]";

    /// <summary>The text clipped to <see cref="ClipLength"/> characters (never inside a surrogate pair, " [truncated]" appended when cut), inside <c>&lt;email&gt;</c> tags; a closing tag inside the text is defanged so the data cannot end its own block.</summary>
    public static string Wrap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var safe = ClosingTag().Replace(text, static m => "< " + m.Value[1..]);
        return $"<email>\n{Clip(safe)}\n</email>";
    }

    private static string Clip(string text)
    {
        if (text.Length <= ClipLength)
        {
            return text;
        }

        var end = ClipLength;
        if (char.IsHighSurrogate(text[end - 1]))
        {
            end--;
        }

        return string.Concat(text.AsSpan(0, end), Truncated);
    }

    [GeneratedRegex(@"</\s*(email|draft)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex ClosingTag();
}
