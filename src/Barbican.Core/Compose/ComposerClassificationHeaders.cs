// Mirrors Bulwark lib/classification/labels.ts, RFC 7444 and RFC 2231.
using System.Text.RegularExpressions;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Compose;

internal static partial class ComposerClassificationHeaders
{
    internal static IReadOnlyDictionary<string, string> Create(ClassificationLabel? label, string extra)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (label is null) return result;
        const string field = "SIO-Label:";
        result["header:SIO-Label:asRaw"] = RawClassification.FormatSioLabel(label, "\r\n")[field.Length..].TrimStart();
        if (!string.IsNullOrEmpty(label.Sensitivity)) result["header:Sensitivity:asText"] = label.Sensitivity;
        if (!string.IsNullOrEmpty(extra) && !string.IsNullOrWhiteSpace(label.ExtraHeaderValue))
            result[$"header:{extra}:asText"] = label.ExtraHeaderValue.Trim();
        return result;
    }

    internal static string ApplySubjectPrefix(string subject, ClassificationLabel? label, IReadOnlyList<ClassificationLabel> labels)
    {
        var prefixes = labels.Select(item => item.SubjectPrefix.Trim()).Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal).OrderByDescending(item => item.Length).ToArray();
        var rest = subject.TrimStart();
        var kept = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var match = prefixes.Select(prefix => Regex.Match(rest, "^" + Regex.Escape(prefix) + @"(?:\s*:)?(?:\s+|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                .FirstOrDefault(found => found.Success);
            if (match is not null) { rest = rest[match.Length..].TrimStart(); continue; }
            var reply = ReplyPrefix().Match(rest);
            if (!reply.Success) break;
            kept.Add(reply.Value.Trim());
            rest = rest[reply.Length..].TrimStart();
        }
        if (rest.Length > 0) kept.Add(rest);
        var chain = string.Join(" ", kept);
        var selected = label?.SubjectPrefix.Trim();
        return string.IsNullOrEmpty(selected) ? chain : chain.Length == 0 ? selected : selected + " " + chain;
    }

    [GeneratedRegex(@"^(?:re|aw|antw|sv|yn|yanit|odp|ответ|resp|vá|回复|回覆|답장|fwd|fw|wg|tr|vs|enc|rv|rvf|inol|pd|pr|ilt|İlt|пересл|пер|转发|轉寄|전달)(?:\[\d+\]|\*\d*)?\s*[:：]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ReplyPrefix();
}
