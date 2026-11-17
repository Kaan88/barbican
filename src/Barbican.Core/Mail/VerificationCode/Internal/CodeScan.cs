// Mirrors: Rust otp.rs:6-279 (normalize, find_code_words, labels_short_number, candidates, window, last_word, labelled_as_other,
// has_promo_words, score, best_candidate) line by line; Bulwark lib/verification-code.ts. Indexes are positions in a Rune array (Rust: Vec<char>).

using System.Text;

namespace Barbican.Core.Mail.VerificationCode.Internal;

/// <summary>The scanner behind <see cref="VerificationCodeFinder"/>.</summary>
internal static class CodeScan
{
    private const int MaxAfter = 120;
    private const int MaxBefore = 60;

    private static readonly HashSet<string> NotACodePrefixes = new(StringComparer.Ordinal)
    {
        "en", "de", "uni", "x", "vs", "op", "byte", "bar", "post", "zip", "qr", "geo", "short", "pseudo",
        "hash", "leet", "trans", "re", "source", "quell", "programm", "program", "status", "error",
        "fehler", "dress", "color", "colour", "farb", "html", "css", "embed", "tracking", "sendungs",
        "referral", "empfehlungs", "sort", "swift", "tax", "ean", "produkt", "product", "artikel",
        "promo", "coupon", "discount", "voucher", "gift", "gutschein", "rabatt", "aktions", "geschenk",
        "bonus", "vorteils", "area", "country", "länder", "laender", "landes", "claude", "studio",
    };

    private static readonly string[] CodeWordEndings =
    [
        "code", "codes", "codice", "codici", "código", "códigos", "codigo", "codigos", "cod", "kod",
        "kodu", "kodem", "kodunuz", "kód", "kódot", "kódu", "kode", "koden", "koodi", "koodia", "kood",
        "kods", "kodas", "код", "кода", "коду", "кодом", "κωδικός", "κωδικό", "קוד", "رمز", "کد",
    ];

    private static readonly HashSet<string> CodeWordsExact = new(StringComparer.Ordinal) { "passcode", "einmalkennwort", "einmalpasswort" };

    private static readonly string[] Phrases =
    [
        "コード", "認証番号", "確認番号", "验证码", "驗證碼", "校验码", "动态码", "인증번호", "인증 번호", "인증코드", "인증 코드", "코드",
        "one-time password", "one time password", "one-time passcode", "one time passcode",
        "single-use password", "single use password", "single-use passcode", "temporary password", "temporary passcode",
    ];

    private static readonly HashSet<string> PromoWords = new(StringComparer.Ordinal) { "off", "rabatt", "save", "discount", "coupon", "voucher", "sale" };
    private static readonly string[] PromoPrefixes = ["spar", "gutschein", "angebot"];

    private static readonly HashSet<string> OtherLabelWords = new(StringComparer.Ordinal)
    {
        "nr", "no", "id", "line", "zeile", "version", "tel", "telefon", "phone", "fax", "mobil", "plz",
        "order", "bestellung", "account", "konto", "invoice", "rechnung", "ticket", "case", "page", "seite", "room", "zimmer",
        "commit", "issue", "pr", "build", "run", "job", "step", "port",
    };

    private static readonly HashSet<string> LinkWords = new(StringComparer.Ordinal) { "is", "lautet", "ist", "est", "es", "è", "é", "är", "er", "jest", "je" };

    private static readonly string[] Currencies = ["eur", "usd", "chf"];

    private static readonly HashSet<string> TanPrefixes = new(StringComparer.Ordinal) { string.Empty, "m", "sms", "push", "chip", "photo", "i", "e" };

    /// <summary>The best code in <paramref name="raw"/>, or null.</summary>
    public static string? BestCandidate(string raw)
    {
        var text = Normalize(raw);
        if (text.Length == 0)
        {
            return null;
        }

        var keywords = FindCodeWords(text);
        if (keywords.Count == 0)
        {
            return null;
        }

        string? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var (s, e) in Candidates(text))
        {
            if (Score(text, s, e, keywords) is { } c && c.Score < bestScore)
            {
                (best, bestScore) = (c.Code, c.Score);
            }
        }

        return best;
    }

    private static bool IsLetter(Rune r) => Rune.IsLetter(r);

    private static bool IsAlnum(Rune r) => Rune.IsLetterOrDigit(r) || Rune.GetUnicodeCategory(r) is System.Globalization.UnicodeCategory.LetterNumber or System.Globalization.UnicodeCategory.OtherNumber;

    private static bool IsAsciiAlnum(Rune r) => r.IsAscii && char.IsAsciiLetterOrDigit((char)r.Value);

    private static bool IsAsciiDigit(Rune r) => r.IsAscii && char.IsAsciiDigit((char)r.Value);

    private static bool Is(Rune r, char c) => r.Value == c;

    private static string Str(Rune[] t, int s, int e)
    {
        var sb = new StringBuilder();
        for (var i = s; i < e; i++)
        {
            sb.Append(t[i].ToString());
        }

        return sb.ToString();
    }

    private static bool IsUppercaseCodeWord(string p)
    {
        if (p is "PIN" or "OTP")
        {
            return true;
        }

        return p.EndsWith("TAN", StringComparison.Ordinal) && TanPrefixes.Contains(p[..^3]);
    }

    private static bool IsCodeWordPart(string part, string? previous)
    {
        if (IsUppercaseCodeWord(part))
        {
            return true;
        }

        var lower = part.ToLowerInvariant();
        if (CodeWordsExact.Contains(lower))
        {
            return true;
        }

        foreach (var ending in CodeWordEndings)
        {
            if (!lower.EndsWith(ending, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = lower[..^ending.Length];
            var qualifier = prefix.Length == 0 ? previous?.ToLowerInvariant() : prefix;
            return qualifier is null || !NotACodePrefixes.Contains(qualifier);
        }

        return false;
    }

    private static Rune[] Normalize(string raw)
    {
        var cleaned = new StringBuilder(raw.Length);
        foreach (var r in raw.EnumerateRunes())
        {
            if (r.Value is not (0xAD or (>= 0x200B and <= 0x200F) or 0x2060 or 0xFEFF or 0x34F))
            {
                cleaned.Append(r.ToString());
            }
        }

        // links and addresses carry ids and tokens, never the code to type
        var lines = cleaned.ToString().Split('\n').Select(line => string.Join(' ', line
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w =>
            {
                var l = w.ToLowerInvariant();
                return !(l.StartsWith("http://", StringComparison.Ordinal) || l.StartsWith("https://", StringComparison.Ordinal) || l.StartsWith("www.", StringComparison.Ordinal) || w.Contains('@', StringComparison.Ordinal));
            })));
        var t = string.Join('\n', lines).Trim();

        // a preview cut off with "..." may end in half a code
        foreach (var ell in new[] { "...", "…" })
        {
            if (t.EndsWith(ell, StringComparison.Ordinal))
            {
                var runes = t[..^ell.Length].EnumerateRunes().ToList();
                while (runes.Count > 0 && (IsAlnum(runes[^1]) || Is(runes[^1], '-')))
                {
                    runes.RemoveAt(runes.Count - 1);
                }

                t = string.Concat(runes.Select(r => r.ToString())).TrimEnd();
            }
        }

        return [.. t.EnumerateRunes()];
    }

    private static List<(int Start, int End)> FindCodeWords(Rune[] text)
    {
        var spans = new List<(int, int)>();
        string? previous = null;
        var i = 0;
        while (i < text.Length)
        {
            if (!IsLetter(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && (IsLetter(text[i]) || (Is(text[i], '-') && i + 1 < text.Length && IsLetter(text[i + 1]))))
            {
                i++;
            }

            var parts = Str(text, start, i).Split('-');
            var hit = false;
            for (var k = 0; k < parts.Length && !hit; k++)
            {
                hit = IsCodeWordPart(parts[k], k > 0 ? parts[k - 1] : previous);
            }

            if (hit)
            {
                var next = Str(text, i, Math.Min(i + 12, text.Length));
                var of = next.ToLowerInvariant().TrimStart().StartsWith("of ", StringComparison.Ordinal) && next.Length > 0 && char.IsWhiteSpace(next[0]);
                if (!of && !LabelsShortNumber(next))
                {
                    spans.Add((start, i));
                }
            }

            previous = parts[^1];
        }

        var lower = text.Select(Rune.ToLowerInvariant).ToArray();
        foreach (var phrase in Phrases)
        {
            var p = phrase.EnumerateRunes().ToArray();
            var k = 0;
            while (k + p.Length <= lower.Length)
            {
                if (lower.AsSpan(k, p.Length).SequenceEqual(p))
                {
                    spans.Add((k, k + p.Length));
                    k += p.Length;
                }
                else
                {
                    k++;
                }
            }
        }

        return spans;
    }

    // "with code 250 (2.1.5)": a code word followed by a 1-3 digit number names a status code.
    private static bool LabelsShortNumber(string next)
    {
        var t = next.TrimStart();
        if (t.StartsWith(':') || t.StartsWith('='))
        {
            t = t[1..];
        }

        t = t.TrimStart();
        var digits = t.TakeWhile(char.IsAsciiDigit).Count();
        if (digits is 0 or > 3)
        {
            return false;
        }

        var rest = t.Skip(digits).Take(2).ToArray();
        if (rest.Length == 0)
        {
            return true;
        }

        if (char.IsLetterOrDigit(rest[0]))
        {
            return false;
        }

        if (rest.Length == 2 && rest[0] is '.' or ',' or ' ' or '-' && char.IsAsciiDigit(rest[1]))
        {
            return false;
        }

        return true;
    }

    // Candidate tokens: hyphen groups (6ZA-BRE), digit groups (557 260), runs of 4-10 ASCII letters and digits.
    private static List<(int Start, int End)> Candidates(Rune[] text)
    {
        var output = new List<(int, int)>();
        var i = 0;
        while (i < text.Length)
        {
            if (!IsAsciiAlnum(text[i]) || (i > 0 && IsAlnum(text[i - 1])))
            {
                i++;
                continue;
            }

            var start = i;
            var j = i;
            while (j < text.Length && (IsAsciiAlnum(text[j]) || (Is(text[j], '-') && j + 1 < text.Length && IsAsciiAlnum(text[j + 1]))))
            {
                j++;
            }

            if (j < text.Length && IsAlnum(text[j]))
            {
                // runs into a non-ASCII letter
                i = j + 1;
                continue;
            }

            var tok = Str(text, start, j);
            var parts = tok.Split('-');
            if (parts.Length is >= 2 and <= 4 && parts.All(p => p.Length is >= 2 and <= 8))
            {
                output.Add((start, j));
            }
            else if (parts.Length == 1)
            {
                var merged = false;
                if (tok.Length is >= 3 and <= 4 && tok.All(char.IsAsciiDigit) && j + 1 < text.Length && Is(text[j], ' '))
                {
                    var k0 = j + 1;
                    var k = k0;
                    while (k < text.Length && IsAsciiDigit(text[k]))
                    {
                        k++;
                    }

                    if (k - k0 is >= 3 and <= 4 && !(k < text.Length && IsAsciiDigit(text[k])))
                    {
                        output.Add((start, k));
                        merged = true;
                    }
                }

                if (!merged && tok.Length is >= 4 and <= 10)
                {
                    output.Add((start, j));
                }
            }

            i = j;
        }

        return output;
    }

    private static string Window(Rune[] text, int from, int to)
    {
        var s = Math.Max(from, 0);
        var e = Math.Min(to, text.Length);
        while (s > 0 && s < e && IsLetter(text[s - 1]) && IsLetter(text[s]))
        {
            s++;
        }

        while (e < text.Length && e > s && IsLetter(text[e]) && IsLetter(text[e - 1]))
        {
            e--;
        }

        return Str(text, s, e);
    }

    private static string LastWord(string s)
    {
        var t = s.TrimEnd(' ', '#', ':', '.');
        var runes = t.EnumerateRunes().ToList();
        var k = runes.Count;
        while (k > 0 && IsLetter(runes[k - 1]))
        {
            k--;
        }

        return string.Concat(runes.Skip(k).Select(r => r.ToString())).ToLowerInvariant();
    }

    private static bool LabelledAsOther(string before)
    {
        var t = before.ToLowerInvariant().TrimEnd(' ', '#');
        if (t.EndsWith("ending in", StringComparison.Ordinal) || t.EndsWith("endet auf", StringComparison.Ordinal) || t.EndsWith("endend auf", StringComparison.Ordinal)
            || t.EndsWith("ends with", StringComparison.Ordinal))
        {
            return true;
        }

        var w = LastWord(t);
        if (w.Length == 0)
        {
            return false;
        }

        return w.EndsWith("nummer", StringComparison.Ordinal) || w.EndsWith("number", StringComparison.Ordinal) || OtherLabelWords.Contains(w);
    }

    private static bool HasPromoWords(string ctx)
    {
        if (ctx.Contains('%', StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var word in Words(ctx))
        {
            var l = word.ToLowerInvariant();
            if (PromoWords.Contains(l) || PromoPrefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Words(string s)
    {
        var sb = new StringBuilder();
        foreach (var r in s.EnumerateRunes())
        {
            if (IsLetter(r))
            {
                sb.Append(r.ToString());
            }
            else if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static (string Code, float Score)? Score(Rune[] text, int start, int end, List<(int Start, int End)> keywords)
    {
        var raw = Str(text, start, end);
        var letters = new string([.. raw.Where(char.IsAsciiLetter)]);
        var digits = new string([.. raw.Where(char.IsAsciiDigit)]);
        var before = Window(text, start - 40, start);
        var after = Str(text, end, Math.Min(end + 8, text.Length));
        if (digits.Length == 0)
        {
            return null;
        }

        if (letters.Length > 0 && !string.Equals(letters, letters.ToUpperInvariant(), StringComparison.Ordinal) && !string.Equals(letters, letters.ToLowerInvariant(), StringComparison.Ordinal))
        {
            return null;
        }

        var code = raw;
        if (letters.Length == 0)
        {
            code = digits;
            if (DigitsRefused(raw, digits, before, after))
            {
                return null;
            }
        }
        else if (MixedRefused(raw))
        {
            return null;
        }

        if (LabelledAsOther(before) || HasPromoWords(Window(text, start - 60, end + 40)))
        {
            return null;
        }

        return Rank(text, start, end, keywords, raw, letters, digits, before, after) is { } s ? (code, s) : null;
    }

    private static bool DigitsRefused(string raw, string digits, string before, string after)
    {
        if (raw.Contains('-', StringComparison.Ordinal) && !(raw.Length <= 9 && raw.Split('-') is { Length: 2 } ps && ps.All(p => p.Length is >= 3 and <= 4)))
        {
            return true;
        }

        if (digits.Length is < 4 or > 10)
        {
            return true;
        }

        static bool Sep(char c) => c is '.' or ',' or ':' or '/' or '-';
        static bool Gap(char c) => c is ' ' or '/' or '-';
        var bc = before;
        var ac = after;
        if ((bc.Length >= 2 && Sep(bc[^1]) && char.IsAsciiDigit(bc[^2])) || (ac.Length >= 2 && Sep(ac[0]) && char.IsAsciiDigit(ac[1])))
        {
            return true;
        }

        if (bc.Length > 0 && bc[^1] is '#' or '+')
        {
            return true;
        }

        // phone numbers, IBAN groups: a digit or ")" right before (one separator allowed), a digit right after
        var bi = bc.Length;
        if (bi > 0 && Gap(bc[bi - 1]))
        {
            bi--;
        }

        if (bi > 0 && (char.IsAsciiDigit(bc[bi - 1]) || bc[bi - 1] == ')'))
        {
            return true;
        }

        var ai = 0;
        if (ai < ac.Length && Gap(ac[ai]))
        {
            ai++;
        }

        if (ai < ac.Length && char.IsAsciiDigit(ac[ai]))
        {
            return true;
        }

        return MoneyAround(before, after);
    }

    // 20 €, $1999, 4000 EUR, 1500%
    private static bool MoneyAround(string before, string after)
    {
        var b = before.TrimEnd();
        if (b.Length > 0 && b[^1] is '€' or '$' or '£')
        {
            return true;
        }

        var a = after.TrimStart().ToLowerInvariant();
        if (a.Length > 0 && a[0] is '€' or '$' or '£' or '%')
        {
            return true;
        }

        return Currencies.Any(c => a.StartsWith(c, StringComparison.Ordinal) && !(a.Length > c.Length && char.IsLetter(a[c.Length])));
    }

    // 10min, 600px; THANKS10; ISO timestamps
    private static bool MixedRefused(string raw)
    {
        var leadDigits = raw.TakeWhile(char.IsAsciiDigit).Count();
        var tail = raw[leadDigits..];
        if (leadDigits > 0 && tail.Length is >= 1 and <= 3 && tail.All(char.IsAsciiLetter))
        {
            return true;
        }

        var leadUpper = raw.TakeWhile(char.IsAsciiLetterUpper).Count();
        var rest = raw[leadUpper..];
        if (leadUpper >= 4 && rest.Length is >= 1 and <= 4 && rest.All(char.IsAsciiDigit))
        {
            return true;
        }

        return raw.Length > 10 && raw[4] == '-' && raw.Contains('T', StringComparison.Ordinal) && raw[..4].All(char.IsAsciiDigit);
    }

    private static float? Rank(Rune[] text, int start, int end, List<(int Start, int End)> keywords, string raw, string letters, string digits, string before, string after)
    {
        var (best, bestAfter) = (float.PositiveInfinity, float.PositiveInfinity);
        foreach (var kw in keywords)
        {
            if (kw.Start < end && kw.End > start)
            {
                return null;
            }

            if (kw.End <= start && start - kw.End <= MaxAfter)
            {
                var d = (float)(start - kw.End);
                bestAfter = Math.Min(bestAfter, d);
                best = Math.Min(best, d);
            }

            if (kw.Start >= end && kw.Start - end <= MaxBefore)
            {
                best = Math.Min(best, (kw.Start - end) * 1.5f);
            }
        }

        if (float.IsInfinity(best))
        {
            return null;
        }

        if (letters.Length > 0 && string.Equals(letters, letters.ToLowerInvariant(), StringComparison.Ordinal))
        {
            if (bestAfter > 40f)
            {
                return null;
            }

            if (raw.All(c => (char.IsAsciiHexDigit(c) && !char.IsAsciiLetterUpper(c)) || c == '-') && bestAfter > 2f)
            {
                return null;
            }
        }

        return Bonus(text, start, end, letters, digits, before, after) is { } bonus ? best - bonus : null;
    }

    // "code: 123456" or a code on its own line ranks higher; a bare year is not a code.
    private static float? Bonus(Rune[] text, int start, int end, string letters, string digits, string before, string after)
    {
        var bt = before.TrimEnd();
        var linked = bt.EndsWith(':') || bt.EndsWith('=') || (bt.Length > 0 && bt.EnumerateRunes().Last() is var lr && IsLetter(lr) && LinkWords.Contains(LastWord(bt)));
        var b2 = before.TrimEnd(' ', '\t');
        var a2 = after.TrimStart(' ', '\t');
        var ownLine = ((b2.Length == 0 && start <= 40) || b2.EndsWith('\n')) && ((a2.Length == 0 && end == text.Length) || a2.StartsWith('\n'));
        var isYear = digits.Length == 4 && (digits.StartsWith("19", StringComparison.Ordinal) || digits.StartsWith("20", StringComparison.Ordinal));
        if (letters.Length == 0 && isYear && !linked && !ownLine)
        {
            return null;
        }

        return (linked ? 15f : 0f) + (ownLine ? 15f : 0f);
    }
}
