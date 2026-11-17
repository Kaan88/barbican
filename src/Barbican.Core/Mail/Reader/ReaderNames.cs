// Mirrors: Rust reader.rs:194 strip_subject_prefixes, :213 forward_subject, :219 sanitize_part, :233 eml_export_name, :241 bundle_name;
// Bulwark lib/subject-prefix.ts, lib/download-filename.ts (sanitizePart, emailExportFilename with the fixed "{date}-{subject}" template, so no
// sender or recipient names leak to whoever receives a forwarded .eml), lib/attachment-name.ts, lib/forward-as-attachment.ts:43,80.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Mail.Reader;

/// <summary>Subjects and file names the reader derives from a message.</summary>
public static class ReaderNames
{
    private static readonly string[] Prefixes = ["re", "fwd", "fw", "aw", "wg"];

    /// <summary>The subject without Re:, Fwd:, Fw:, AW:, WG: prefixes (with an optional [n]).</summary>
    public static string StripSubjectPrefixes(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var s = subject.Trim();
        while (true)
        {
            var lower = AsciiLower(s);
            int? cut = null;
            foreach (var p in Prefixes)
            {
                if (!lower.StartsWith(p, StringComparison.Ordinal))
                {
                    continue;
                }

                var r2 = lower[p.Length..].TrimStart();
                var r3 = r2.StartsWith('[') && r2.IndexOf(']', StringComparison.Ordinal) is var close and >= 0 ? r2[(close + 1)..].TrimStart() : r2;
                if (r3.StartsWith(':'))
                {
                    cut = s.Length - (r3.Length - 1);
                    break;
                }
            }

            if (cut is not { } i)
            {
                return s;
            }

            s = s[i..].Trim();
        }
    }

    /// <summary>"Fwd: subject" with its prefixes stripped; empty for a message without a subject.</summary>
    public static string ForwardSubject(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var stripped = StripSubjectPrefixes(subject);
        return subject.Trim().Length == 0 ? string.Empty : stripped.Length == 0 ? "Fwd:" : "Fwd: " + stripped;
    }

    /// <summary>Characters a file cannot have become "_", runs collapse, the edges are trimmed, at most <paramref name="max"/> characters.</summary>
    public static string SanitizePart(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var c = "<>:\"/\\|?*".Contains(rune.ToString(), StringComparison.Ordinal) || Rune.IsControl(rune) ? new Rune('_') : rune;
            if (c.Value == '_' && sb.Length > 0 && sb[^1] == '_')
            {
                continue;
            }

            if (Rune.IsWhiteSpace(c))
            {
                if (sb.Length == 0 || sb[^1] != ' ')
                {
                    sb.Append(' ');
                }

                continue;
            }

            sb.Append(c.ToString());
        }

        var t = sb.ToString().Trim().Trim('.', '_', '-');
        var output = new StringBuilder();
        var n = 0;
        foreach (var r in t.EnumerateRunes())
        {
            if (n++ >= max)
            {
                break;
            }

            output.Append(r.ToString());
        }

        return output.ToString();
    }

    /// <summary>"attachments_subject.zip", or "attachments.zip" for an empty subject.</summary>
    public static string BundleName(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var stem = SanitizePart(subject.Trim(), 200);
        return stem.Length == 0 ? "attachments.zip" : $"attachments_{stem}.zip";
    }

    /// <summary>"yyyy-MM-dd HH.mm.ss-subject.eml" from receivedAt (or sentAt) and the subject.</summary>
    public static string EmlExportName(JsonNode message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var at = Time(message.Str("receivedAt")) ?? Time(message.Str("sentAt"));
        return EmlExportName(message.Str("subject"), at, TimeZoneInfo.Local);
    }

    /// <summary>The same name from typed values; the time is shown in <paramref name="zone"/> (Rust: the local zone).</summary>
    public static string EmlExportName(string? subject, DateTimeOffset? at, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var date = at is { } t ? TimeZoneInfo.ConvertTime(t, zone).ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture) : "0000-00-00 00.00.00";
        var stem = SanitizePart($"{date}-{subject ?? string.Empty}", 200);
        return (stem.Length == 0 ? "email" : stem) + ".eml";
    }

    // Rust to_ascii_lowercase: only A-Z change, so offsets in the lower-cased copy are offsets in the original.
    private static string AsciiLower(string s) => string.Create(s.Length, s, static (span, src) =>
    {
        for (var i = 0; i < src.Length; i++)
        {
            span[i] = char.IsAsciiLetterUpper(src[i]) ? (char)(src[i] | 0x20) : src[i];
        }
    });

    private static DateTimeOffset? Time(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;
}
