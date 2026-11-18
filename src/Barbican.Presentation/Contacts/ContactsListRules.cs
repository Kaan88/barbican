// Mirrors: Rust contacts.rs:83 sort_name, full_name, cal_editor.rs:69 initials; Bulwark components/contacts/contact-list.tsx (getContactSortName, letter groups, search). B-CON-004, B-CON-027, B-CON-030. Contract from the M5 plan section 4.5; owner con-ui.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Presentation.Contacts;

/// <summary>The pure rules of the contact list.</summary>
public static class ContactsListRules
{
    private static readonly char[] WordSeparators = [' ', '.', '_', '-'];

    /// <summary>The display name: the full name, else the components joined (prefix, given, middle, surname, suffix), else the first email, else the nickname or organization.</summary>
    public static string FullName(JsonNode card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var full = ContactsJson.Str(card["name"], "full").Trim();
        if (full.Length > 0)
        {
            return full;
        }

        var joined = string.Join(' ', Parts(card, "prefix", "title").Concat(Parts(card, "given")).Concat(Parts(card, "given2", "middle", "additional")).Concat(Parts(card, "surname", "surname2")).Concat(Parts(card, "suffix", "credential", "generation")));
        if (joined.Length > 0)
        {
            return joined;
        }

        var email = ContactsJson.Emails(card).FirstOrDefault();
        if (email is not null)
        {
            return email;
        }

        var nick = ContactsJson.FirstOf(card["nicknames"], "name");
        return nick.Length > 0 ? nick : ContactsJson.FirstOf(card["organizations"], "name");
    }

    /// <summary>The sort key: the display name, or with <paramref name="byLast"/> "Surname, given middle" from the name components (surname and surname2; given, given2, middle, additional), or from the last word of a plain full name ("Ada Lovelace" is "Lovelace, Ada").</summary>
    public static string SortName(JsonNode card, bool byLast)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!byLast)
        {
            return FullName(card);
        }

        var surname = string.Join(' ', Parts(card, "surname", "surname2"));
        if (surname.Length > 0)
        {
            var given = string.Join(' ', Parts(card, "given", "given2", "middle", "additional"));
            return given.Length > 0 ? surname + ", " + given : surname;
        }

        var name = FullName(card);
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length < 2 || name.Contains('@', StringComparison.Ordinal) ? name : words[^1] + ", " + string.Join(' ', words[..^1]);
    }

    /// <summary>The letter header of a sort key: its first letter upper-case with diacritics stripped, "#" for a digit or symbol.</summary>
    public static string LetterOf(string sortKey)
    {
        var stripped = Fold(sortKey ?? string.Empty).TrimStart();
        return stripped.Length > 0 && char.IsLetter(stripped[0]) ? char.ToUpperInvariant(stripped[0]).ToString() : "#";
    }

    /// <summary>Avatar initials without a photo: two letters of the first two words of the name (words split at space, dot, underscore, hyphen), the first two letters of a single word, the part of the email before the @ when there is no name, "?" for nothing; upper-case.</summary>
    public static string Initials(string name, string email)
    {
        var source = (name ?? string.Empty).Trim();
        if (source.Length == 0)
        {
            var mail = (email ?? string.Empty).Trim();
            var at = mail.IndexOf('@', StringComparison.Ordinal);
            source = at >= 0 ? mail[..at] : mail;
        }

        var words = source.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => First(words[0], 2),
            _ => First(words[0], 1) + First(words[1], 1),
        };
    }

    /// <summary>Whether a card matches the search text (every word of the text in the name, an email, a phone, an organization or a note; case and diacritics ignored).</summary>
    public static bool Matches(JsonNode card, string query)
    {
        ArgumentNullException.ThrowIfNull(card);
        var words = Fold(query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }

        var hay = Fold(string.Join(' ', Haystack(card)));
        return words.All(w => hay.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>Lower-case with diacritics removed (the form every comparison of this folder uses).</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var d = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                _ = sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString().Replace("ß", "ss", StringComparison.Ordinal);
    }

    private static string First(string word, int count)
    {
        var folded = Fold(word);
        var take = folded.Length >= count ? count : folded.Length;
        return take == 0 ? "?" : folded[..take].ToUpperInvariant();
    }

    private static IEnumerable<string> Parts(JsonNode card, params string[] kinds)
    {
        if (card["name"]?["components"] is not JsonArray components)
        {
            yield break;
        }

        foreach (var kind in kinds)
        {
            foreach (var c in components)
            {
                if (string.Equals(ContactsJson.Str(c, "kind"), kind, StringComparison.Ordinal) && ContactsJson.Str(c, "value").Trim() is { Length: > 0 } v)
                {
                    yield return v;
                }
            }
        }
    }

    private static IEnumerable<string> Haystack(JsonNode card)
    {
        yield return FullName(card);
        yield return ContactsJson.Str(card["name"], "full");
        foreach (var e in ContactsJson.Emails(card))
        {
            yield return e;
        }

        foreach (var p in ContactsJson.All(card["phones"], "number"))
        {
            yield return p;
        }

        foreach (var o in ContactsJson.All(card["organizations"], "name"))
        {
            yield return o;
        }

        foreach (var n in ContactsJson.All(card["nicknames"], "name"))
        {
            yield return n;
        }

        foreach (var n in ContactsJson.All(card["notes"], "note"))
        {
            yield return n;
        }
    }
}
