// Mirrors: Rust mailview.rs:326 on_ipc (tag rust-final): "shell", "ready:N", "key:...", "dirty"; M2 adds "sel:TEXT" (onTextSelectionChange).
//   New in the C# port (docs/adr/0005 decision 4, "M0 review"): "link:URL" (links routed by the shell
//   on both OSes) and "ctx:X,Y" (already posted by the Rust shell, unused there), plus an allow-list
//   and length caps, because on Linux the messages arrive through webkit.messageHandlers.unoWebView
//   from a page that also hosts untrusted mail frames.
// Bulwark: none (no host bridge in a browser). RFC 3986 §3.1 (scheme), RFC 6068 (mailto).
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Barbican.Core.Mail.Body;

/// <summary>
/// One validated page → host message of the body shell. <see cref="TryParse"/> is the only way in:
/// unknown, malformed, oversized or out-of-range input is rejected (and counted by
/// <see cref="ShellMessageGate"/>). A <see cref="ShellMessageKind.Link"/> carries an absolute
/// http, https or mailto URI without control characters or white space.
/// </summary>
public sealed record ShellMessage(ShellMessageKind Kind, long Sequence = 0, string? Key = null, int X = 0, int Y = 0, Uri? Link = null, string? Text = null)
{
    /// <summary>Longest message accepted at all (a link URL dominates).</summary>
    public const int MaxLength = 8192;

    /// <summary>Longest <c>key:</c> payload: up to three flag letters, the bar and a key name.</summary>
    public const int MaxKeyLength = 32;

    /// <summary>Longest selected text passed on (<c>sel:</c>); the shell cuts it to this as well.</summary>
    public const int MaxSelection = 4000;

    /// <summary>Largest page coordinate accepted in <c>ctx:X,Y</c>.</summary>
    public const int MaxCoordinate = 1_000_000;

    private static readonly string[] LinkSchemes = ["http", "https", "mailto"];

    public static bool TryParse(string? raw, [NotNullWhen(true)] out ShellMessage? message)
    {
        message = null;
        if (string.IsNullOrEmpty(raw) || raw.Length > MaxLength)
        {
            return false;
        }

        if (string.Equals(raw, "shell", StringComparison.Ordinal))
        {
            message = new ShellMessage(ShellMessageKind.Shell);
            return true;
        }

        if (string.Equals(raw, "dirty", StringComparison.Ordinal))
        {
            message = new ShellMessage(ShellMessageKind.Dirty);
            return true;
        }

        message = WithPayload(raw);
        return message is not null;
    }

    private static ShellMessage? WithPayload(string raw)
    {
        if (raw.StartsWith("ready:", StringComparison.Ordinal))
        {
            var n = raw.AsSpan(6);
            return n.Length is > 0 and <= 18 && IsDigits(n)
                && long.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) && seq > 0
                ? new ShellMessage(ShellMessageKind.Ready, Sequence: seq)
                : null;
        }

        if (raw.StartsWith("key:", StringComparison.Ordinal))
        {
            var k = raw[4..];
            return IsKeyPayload(k) ? new ShellMessage(ShellMessageKind.Key, Key: k) : null;
        }

        if (raw.StartsWith("ctx:", StringComparison.Ordinal))
        {
            var parts = raw[4..].Split(',');
            return parts.Length == 2 && Coordinate(parts[0]) is { } x && Coordinate(parts[1]) is { } y
                ? new ShellMessage(ShellMessageKind.Context, X: x, Y: y)
                : null;
        }

        if (raw.StartsWith("sel:", StringComparison.Ordinal))
        {
            var t = raw[4..];
            return t.Length <= MaxSelection ? new ShellMessage(ShellMessageKind.Selection, Text: new string([.. t.Select(c => char.IsControl(c) && c is not '\n' and not '\t' ? ' ' : c)])) : null;
        }

        return raw.StartsWith("link:", StringComparison.Ordinal) && TryLink(raw[5..], out var uri)
            ? new ShellMessage(ShellMessageKind.Link, Link: uri)
            : null;
    }

    /// <summary>The host's scheme check for a link from a message: absolute http, https or mailto only.</summary>
    public static bool TryLink(string? text, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaxLength)
        {
            return false;
        }

        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        foreach (var scheme in LinkSchemes)
        {
            if (string.Equals(parsed.Scheme, scheme, StringComparison.OrdinalIgnoreCase))
            {
                uri = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool IsDigits(ReadOnlySpan<char> s)
    {
        foreach (var c in s)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static int? Coordinate(string s)
    {
        var span = s.AsSpan();
        var negative = span.Length > 0 && span[0] == '-';
        var digits = negative ? span[1..] : span;
        if (digits.Length is 0 or > 7 || !IsDigits(digits))
        {
            return null;
        }

        var v = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        if (v > MaxCoordinate)
        {
            return null;
        }

        return negative ? -v : v;
    }

    // FLAGS|KEY with FLAGS a subsequence of "csa" (the order shell.js writes) and KEY one printable token.
    private static bool IsKeyPayload(string k)
    {
        if (k.Length is < 2 or > MaxKeyLength)
        {
            return false;
        }

        var bar = k.IndexOf('|', StringComparison.Ordinal);
        if (bar is < 0 or > 3)
        {
            return false;
        }

        var next = 0;
        for (var i = 0; i < bar; i++)
        {
            var at = "csa".IndexOf(k[i], next);
            if (at < 0)
            {
                return false;
            }

            next = at + 1;
        }

        var key = k.AsSpan(bar + 1);
        if (key.IsEmpty)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                return false;
            }
        }

        return true;
    }
}
