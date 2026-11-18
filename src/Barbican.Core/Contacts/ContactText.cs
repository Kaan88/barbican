// Text comparison keys for contacts: case, accents and spacing do not count. Mirrors Rust contact_merge.rs:7 fold (the common Latin letters), made complete with Unicode decomposition; Bulwark lib/contacts/merge.ts normalises the same way. Owner con-data.

using System.Globalization;
using System.Text;

namespace Barbican.Core.Contacts;

/// <summary>Accent-insensitive, case-insensitive keys.</summary>
internal static class ContactText
{
    /// <summary>Lower case, accents removed ("Müller" is "muller", "Łukasz" is "lukasz"), runs of white space one space, no white space at the ends.</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // letters that do not decompose
            _ = sb.Append(c switch { 'ø' or 'Ø' => 'o', 'ł' or 'Ł' => 'l', 'ı' => 'i', 'ß' => 's', 'đ' or 'Đ' => 'd', _ => c });
        }

        return string.Join(' ', sb.ToString().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
