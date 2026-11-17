// Mirrors: Rust mail.rs (initials :347, tint :353, time_label_with :373, sender :401, clean_preview :1128, attachment_names :552);
// Bulwark components/email/thread-list-item.tsx (sender, preview, attachment chips), lib/list-date-groups.ts. Owner mailcore.

using System.Globalization;
using System.Text;
using Barbican.Core.Mail.Grouping;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Rules;

/// <summary>The words a list row shows, worked out from the message's data.</summary>
public static class RowText
{
    private static readonly uint[] TintPalette = [0xf0577a, 0x60a5fa, 0x34d399, 0xfbbf24];

    /// <summary>The sender as the row names them: the display name, else the address, else "(unknown)".</summary>
    public static string Sender(EmailSummary email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var from = email.From.Count > 0 ? email.From[0] : null;
        if (from is null)
        {
            return "(unknown)";
        }

        return !string.IsNullOrEmpty(from.Name) ? from.Name : !string.IsNullOrEmpty(from.Email) ? from.Email : "(unknown)";
    }

    /// <summary>Up to two letters or digits of a name, upper case; "?" when it has none.</summary>
    public static string Initials(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var letters = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                letters.Append(rune.ToString());
                if (letters.Length >= 2)
                {
                    break;
                }
            }
        }

        return letters.Length == 0 ? "?" : letters.ToString().ToUpperInvariant();
    }

    /// <summary>The avatar tint index (0..3: rose, sky, mint, amber) a name hashes to (Rust <c>tint</c>: h = h*31 + byte, seeded with 7).</summary>
    public static int TintIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var h = 7u;
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            h = unchecked((h * 31) + b);
        }

        return (int)(h % (uint)TintPalette.Length);
    }

    /// <summary>The 0xRRGGBB of <see cref="TintIndex"/>.</summary>
    public static uint TintRgb(string name) => TintPalette[TintIndex(name)];

    /// <summary>The preview with invisible characters dropped and whitespace folded; a placeholder when nothing is left.</summary>
    public static string CleanPreview(string? preview)
    {
        var sb = new StringBuilder();
        var pendingSpace = false;
        foreach (var rune in (preview ?? string.Empty).EnumerateRunes())
        {
            if (IsInvisible(rune.Value))
            {
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(rune.ToString());
        }

        return sb.Length == 0 ? "No preview available" : sb.ToString();
    }

    /// <summary>The names of the attachments a row's chips show; inline images are left out when asked.</summary>
    public static IReadOnlyList<string> AttachmentNames(EmailSummary email, bool hideInlineImages)
    {
        ArgumentNullException.ThrowIfNull(email);
        return [.. email.Attachments
            .Where(a => !(hideInlineImages && IsInlineImage(a)))
            .Select(a => string.IsNullOrEmpty(a.Name) ? "attachment" : a.Name)];
    }

    /// <summary>The time column: <paramref name="format"/> is <c>smart</c>, <c>relative</c> or <c>full</c> (Bulwark dateFormat).</summary>
    public static string TimeLabel(DateTimeOffset at, DateTimeOffset now, TimeZoneInfo zone, string format, bool twelveHour)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(format);
        var local = TimeZoneInfo.ConvertTime(at, zone);
        var hm = twelveHour
            ? local.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : local.ToString("HH:mm", CultureInfo.InvariantCulture);
        var days = DateGroups.DayOf(now, zone).DayNumber - DateGroups.DayOf(at, zone).DayNumber;
        switch (format)
        {
            case "relative":
                return days switch
                {
                    0 => hm,
                    1 => "Yesterday",
                    >= 2 and <= 6 => string.Create(CultureInfo.InvariantCulture, $"{days} days ago"),
                    _ => local.ToString("dd MMM", CultureInfo.InvariantCulture),
                };
            case "full":
                return local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + " " + hm;
            default:
                return days switch
                {
                    0 => hm,
                    >= 1 and <= 6 => local.ToString("ddd", CultureInfo.InvariantCulture) + " " + hm,
                    _ => local.ToString("dd MMM", CultureInfo.InvariantCulture),
                };
        }
    }

    private static bool IsInlineImage(Attachment a) =>
        a.Cid is not null && (a.Type ?? string.Empty).StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static bool IsInvisible(int c) =>
        c is 0x00AD or 0x034F or 0x061C or 0x115F or 0x1160 or 0x17B4 or 0x17B5
        or (>= 0x180B and <= 0x180F) or (>= 0x200B and <= 0x200F) or (>= 0x202A and <= 0x202E) or (>= 0x2060 and <= 0x206F)
        or 0x3164 or (>= 0xFE00 and <= 0xFE0F) or 0xFEFF or 0xFFA0 or (>= 0xE0000 and <= 0xE007F);
}
