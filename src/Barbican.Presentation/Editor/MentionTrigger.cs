// Mirrors: Bulwark components/email/recipient-mention.tsx (char "@", allowedPrefixes " " and nbsp so info@example.com never triggers; not in code; the list opens only while someone matches).

namespace Barbican.Presentation.Editor;

/// <summary>The "@query" being typed before the caret.</summary>
public readonly record struct MentionTrigger(int Start, string Query)
{
    /// <summary>The trigger at <paramref name="caret"/> in <paramref name="text"/>, or null: an "@" at the start of the text or after a space, then no white space up to the caret.</summary>
    public static MentionTrigger? Find(string text, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        caret = Math.Clamp(caret, 0, text.Length);
        for (var i = caret - 1; i >= 0; i--)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                return null;
            }

            if (c == '@')
            {
                return i == 0 || text[i - 1] is ' ' or ' ' ? new MentionTrigger(i, text[(i + 1)..caret]) : null;
            }
        }

        return null;
    }

    /// <summary>What a pick inserts: the first name (Bulwark's label), else the part of the address before the "@".</summary>
    public static string Label(MentionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var first = candidate.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (!string.IsNullOrEmpty(first))
        {
            return first;
        }

        var at = candidate.Email.IndexOf('@', StringComparison.Ordinal);
        return at > 0 ? candidate.Email[..at] : candidate.Email;
    }
}
