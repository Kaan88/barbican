// Mirrors: Rust mailto.rs:33 decode_lenient, :62 parse, :115 keep_pending, :121 sign_in_message, limits :20-22; Bulwark lib/protocol-handlers/mailto.ts (parseMailtoUrl); RFC 6068. Port by M3 composer-ui.

using System.Text;
using Barbican.Core.Compose.Recipients;

namespace Barbican.Core.Mailto;

/// <summary>RFC 6068 parsing with the desktop app's limits.</summary>
public static class MailtoLink
{
    public const int MaxRecipients = 200;

    public const int MaxSubject = 998;

    public const int MaxBody = 64 * 1024;

    /// <summary>Links kept while no account is signed in (the oldest are dropped).</summary>
    public const int MaxPending = 5;

    /// <summary>Percent-decoding that never fails: a stray percent stays, broken UTF-8 becomes U+FFFD.</summary>
    public static string DecodeLenient(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var b = Encoding.UTF8.GetBytes(value);
        var output = new List<byte>(b.Length);
        for (var i = 0; i < b.Length; i++)
        {
            if (b[i] == (byte)'%' && i + 2 < b.Length && Hex(b[i + 1]) is { } h && Hex(b[i + 2]) is { } l)
            {
                output.Add((byte)((h * 16) + l));
                i += 2;
                continue;
            }

            output.Add(b[i]);
        }

        return Encoding.UTF8.GetString([.. output]);
    }

    /// <summary>
    /// A mailto: link (scheme case-insensitive) as a <see cref="MailtoFields"/>; null for anything else. Recipients split on commas (quoted names respected), <c>to</c>/<c>cc</c>/<c>bcc</c> parameters in any case, repeated
    /// parameters appended, the fragment ignored (an encoded # stays), control characters dropped from the subject and recipients, CRLF in the body normalised to LF, at most <see cref="MaxRecipients"/> recipients in all (Cc and Bcc get what To leaves, so a long To empties them), <see cref="MaxSubject"/> and <see cref="MaxBody"/> limits. A plus sign stays a plus.
    /// </summary>
    public static MailtoFields? Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var s = raw.Trim();
        if (s.Length < 7 || !s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = s[7..];
        var hash = rest.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            rest = rest[..hash];
        }

        var q = rest.IndexOf('?', StringComparison.Ordinal);
        var path = q >= 0 ? rest[..q] : rest;
        var query = q >= 0 ? rest[(q + 1)..] : "";
        var prms = new List<(string Key, string Value)>();
        foreach (var p in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = p.IndexOf('=', StringComparison.Ordinal);
            prms.Add(eq >= 0 ? (DecodeLenient(p[..eq]), DecodeLenient(p[(eq + 1)..])) : (DecodeLenient(p), ""));
        }

        string Get(string key) => string.Join(',', prms.Where(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value));

        var to = Split(DecodeLenient(path)).Concat(Split(Get("to"))).Take(MaxRecipients).ToList();
        var cc = Split(Get("cc")).Take(MaxRecipients - to.Count).ToList();
        var bcc = Split(Get("bcc")).Take(MaxRecipients - to.Count - cc.Count).ToList();
        return new MailtoFields
        {
            To = to,
            Cc = cc,
            Bcc = bcc,
            Subject = Truncate(StripControl(Get("subject")), MaxSubject),
            Body = CleanBody(Get("body")),
            Extra = ComposeLinkParameters.Parse(prms.Select(p => KeyValuePair.Create(p.Key, p.Value))),
        };
    }

    /// <summary>Parses a desktop new-message URI; only add-on parameters are carried into the new draft.</summary>
    public static MailtoFields? ParseComposeUri(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "bulwark", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "new", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort ||
            uri.AbsolutePath is not ("" or "/") || !string.IsNullOrEmpty(uri.Fragment)) return null;
        var query = uri.Query.TrimStart('?');
        var parameters = query.Split('&', StringSplitOptions.RemoveEmptyEntries).Select(part =>
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? part : part[..equals];
            var value = equals < 0 ? "" : part[(equals + 1)..];
            return KeyValuePair.Create(DecodeLenient(key.Replace('+', ' ')), DecodeLenient(value.Replace('+', ' ')));
        });
        return new MailtoFields { Extra = ComposeLinkParameters.Parse(parameters) };
    }

    /// <summary>Removes add-on query parameters from a link sent to a general mailto observer.</summary>
    public static string WithoutAddOnParameters(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var value = raw.Split('#', 2)[0];
        var question = value.IndexOf('?', StringComparison.Ordinal);
        if (question < 0) return value;
        var standard = value[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).Where(part =>
            !DecodeLenient(part.Split('=', 2)[0]).Trim().StartsWith("x-", StringComparison.OrdinalIgnoreCase));
        var rest = string.Join('&', standard);
        return rest.Length > 0 ? value[..(question + 1)] + rest : value[..question];
    }

    /// <summary>Appends a link to the pending list, keeping the newest <see cref="MaxPending"/>.</summary>
    public static void KeepPending(IList<MailtoFields> list, MailtoFields link)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(link);
        list.Add(link);
        while (list.Count > MaxPending)
        {
            list.RemoveAt(0);
        }
    }

    /// <summary>"Sign in to write to a@x.example." (the first recipient; "Sign in to write this message." without one).</summary>
    public static string SignInMessage(MailtoFields link)
    {
        ArgumentNullException.ThrowIfNull(link);
        return link.To.Count > 0 ? $"Sign in to write to {link.To[0]}." : "Sign in to write this message.";
    }

    private static int? Hex(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => null,
    };

    private static string StripControl(string v) => new([.. v.Where(c => !char.IsControl(c))]);

    private static string Truncate(string v, int max) => v.Length <= max ? v : v[..max];

    private static string CleanBody(string v)
    {
        var t = v.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return Truncate(new string([.. t.Where(c => c is '\n' or '\t' || !char.IsControl(c))]), MaxBody);
    }

    private static IReadOnlyList<string> Split(string v) => RecipientText.SplitRecipients(StripControl(v), ",");
}
