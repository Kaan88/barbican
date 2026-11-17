// Mirrors: Rust spam.rs:29 analyze (X-Spam-Status score= and required=, X-Spam-Score, X-Rspamd-Score, the X-Spamd-Result bracket [score / required],
// X-Spam-Flag, X-Rspamd-Action / X-Spam-Action, X-Spam-LLM, X-Spam-Level stars), :8 get, :15 num_after, :21 bracket; the same file in
// tests/fixtures/plugins/spam-score/src/spam.rs; the row text of tests/fixtures/plugins/spam-score/src/lib.rs; Bulwark's spam-score plugin. Owner unified.

using System.Globalization;
using System.Text;
using Barbican.Plugins;

namespace Barbican.Plugin.SpamScore;

/// <summary>Reads the spam headers servers add and gives a verdict (parity with Rust spam.rs).</summary>
public static class SpamAnalyzer
{
    private const string Dot = "  ·  ";
    private static readonly string[] SpamActions = ["reject", "add header", "rewrite subject", "quarantine", "spam"];
    private static readonly string[] LlmVerdicts = ["LEGITIMATE", "SPAM", "SUSPICIOUS"];

    /// <summary>The verdict, or null when the message carries none of the spam headers.</summary>
    public static SpamAnalysis? Analyze(IReadOnlyList<HeaderView> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var status = Get(headers, "X-Spam-Status");
        var br = Get(headers, "X-Spamd-Result") is { } r ? Bracket(r) : null;
        var score = (status is null ? null : NumAfter(status, "score="))
            ?? Parse(Get(headers, "X-Spam-Score"))
            ?? Parse(Get(headers, "X-Rspamd-Score"))
            ?? br?.Score;
        var required = (status is null ? null : NumAfter(status, "required=")) ?? br?.Required;
        var flag = Get(headers, "X-Spam-Flag") is { } f && (f.Equals("yes", StringComparison.OrdinalIgnoreCase) || f.Equals("true", StringComparison.OrdinalIgnoreCase));
        var action = Get(headers, "X-Rspamd-Action") ?? Get(headers, "X-Spam-Action");
        var actionSpam = action is not null && SpamActions.Any(k => action.ToLowerInvariant().Contains(k, StringComparison.Ordinal));
        string? llmKind = null;
        string? llmRaw = null;
        if (Get(headers, "X-Spam-LLM") is { } v)
        {
            var up = v.ToUpperInvariant();
            llmKind = LlmVerdicts.FirstOrDefault(k => up.StartsWith(k, StringComparison.Ordinal));
            llmRaw = llmKind is null ? null : v;
        }

        if (score is null && !flag && action is null && llmKind is null)
        {
            return null;
        }

        var verdict = Verdict(score, required, flag || actionSpam, llmKind);
        return new SpamAnalysis(verdict, Detail(headers, score, required, action, llmRaw));
    }

    /// <summary>The row of the email-details hook (the Rust plugin's barbican_plugin_call in lib.rs), or none without spam headers.</summary>
    public static IReadOnlyList<EmailDetailRow> Rows(IReadOnlyList<HeaderView> headers)
    {
        if (Analyze(headers) is not { } s)
        {
            return [];
        }

        var (word, tone) = s.Verdict switch
        {
            "spam" => ("Spam", "danger"),
            "suspicious" => ("Suspicious", "warning"),
            _ => ("Clean", "success"),
        };
        return [new EmailDetailRow("Spam analysis", word + Dot + s.Detail, tone)];
    }

    // spam.rs:46-67: a flag or a spam action wins; then the score against the threshold; an AI verdict can only raise a clean one
    private static string Verdict(double? score, double? required, bool flagged, string? llm)
    {
        var verdict = flagged ? "spam"
            : score is { } s0 && required is { } r0 && s0 >= r0 ? "spam"
            : score is { } s1 && required is { } r1 && s1 >= 0.6 * r1 ? "suspicious"
            : "clean";
        return !string.Equals(verdict, "clean", StringComparison.Ordinal) ? verdict : llm switch
        {
            "SPAM" => "spam",
            "SUSPICIOUS" => "suspicious",
            _ => verdict,
        };
    }

    // spam.rs:68-82: score, stars, action and the AI text, joined by a spaced middle dot
    private static string Detail(IReadOnlyList<HeaderView> headers, double? score, double? required, string? action, string? llm)
    {
        var parts = new List<string>();
        if (score is { } sc)
        {
            parts.Add(required is { } rq
                ? string.Create(CultureInfo.InvariantCulture, $"Score {sc:F2} / {rq:F2} threshold")
                : string.Create(CultureInfo.InvariantCulture, $"Score {sc:F2}"));
        }

        int? stars = Get(headers, "X-Spam-Level") is { } level
            ? level.Count(c => c == '*')
            : score is { } s2 ? (int)Math.Min(Math.Floor(Math.Max(s2, 0.0)), 15.0) : null;
        if (stars is > 0)
        {
            parts.Add(new StringBuilder().Insert(0, "★", stars.Value).ToString());
        }

        if (action is not null)
        {
            parts.Add($"Action: {action}");
        }

        if (llm is not null)
        {
            parts.Add($"AI: {llm}");
        }

        return string.Join(Dot, parts);
    }

    private static string? Get(IReadOnlyList<HeaderView> headers, string name) =>
        headers.FirstOrDefault(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();

    private static double? Parse(string? text) =>
        text is not null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static double? NumAfter(string s, string key)
    {
        var i = s.IndexOf(key, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        i += key.Length;
        var end = i;
        while (end < s.Length && (char.IsAsciiDigit(s[end]) || s[end] is '.' or '-'))
        {
            end++;
        }

        return Parse(s[i..end]);
    }

    private static (double Score, double Required)? Bracket(string s)
    {
        var a = s.IndexOf('[', StringComparison.Ordinal);
        if (a < 0)
        {
            return null;
        }

        var b = s.IndexOf(']', a);
        if (b < 0)
        {
            return null;
        }

        var parts = s[(a + 1)..b].Split('/');
        return parts.Length >= 2 && Parse(parts[0].Trim()) is { } x && Parse(parts[1].Trim()) is { } y ? (x, y) : null;
    }
}
