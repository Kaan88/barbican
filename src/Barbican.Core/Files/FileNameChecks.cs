// Mirrors: Bulwark lib/file-name-rules.ts (fileNameRulesFrom :37, fileNameProblem :84, acceptedFileName :148, plainFileNameProblem :95); Stalwart crates/jmap/src/file/ (validate_name: control characters and a reserved stem are refused
// on every release; 0.16.6+ publishes forbiddenNameChars, forbiddenNodeNames and maxSizeFileNodeName in the filenode capability). B-FILE-004; G-019. Owner files-data.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Files;

/// <summary>Checks names against the rules of the server before anything is uploaded.</summary>
public static class FileNameChecks
{
    /// <summary>The rules of a filenode capability object; null when it publishes none.</summary>
    public static FileNameRules? RulesFrom(JsonNode? capability)
    {
        if (capability is not JsonObject)
        {
            return null;
        }

        var chars = capability.Str("forbiddenNameChars") ?? string.Empty;
        IReadOnlyList<string> names = capability.Arr("forbiddenNodeNames") is { } a ? [.. a.Select(x => x.AsString()).OfType<string>()] : [];
        if (chars.Length == 0 && names.Count == 0)
        {
            return null;
        }

        var max = capability["maxSizeFileNodeName"] is JsonValue v && v.TryGetValue<double>(out var d) && d > 0 ? (int)Math.Min(d, int.MaxValue) : FileNameRules.DefaultMaxNameBytes;
        return new FileNameRules(chars, names, max);
    }

    /// <summary>The first problem of a name, or null: longer than the limit in UTF-8 bytes (tooLong), a forbidden character (chars), a control character U+0000-001F or U+007F-009F (control), a reserved name or reserved stem before the first dot compared ignoring ASCII case ("con.txt": reserved). Null rules: no problem.</summary>
    public static FileNameProblem? ProblemOf(string name, FileNameRules? rules)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (rules is null)
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(name) > rules.MaxNameBytes)
        {
            return new FileNameProblem("tooLong", string.Empty);
        }

        var bad = Runes(name).Where(r => rules.ForbiddenChars.Contains(r, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
        if (bad.Count > 0)
        {
            return new FileNameProblem("chars", string.Join(' ', bad));
        }

        if (Runes(name).Any(IsControl))
        {
            return new FileNameProblem("control", string.Empty);
        }

        return IsReserved(name, rules.ForbiddenNames) ? new FileNameProblem("reserved", string.Empty) : null;
    }

    /// <summary>The words for a problem, for a dialog or a toast.</summary>
    public static string Message(FileNameProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem.Kind switch
        {
            "chars" => $"Name contains a forbidden character ({problem.Detail}).",
            "control" => "Name contains a control character.",
            "tooLong" => "Name is too long.",
            "reserved" => "Name is reserved and cannot be used.",
            _ => "Name is not allowed.",
        };
    }

    /// <summary>A variant of <paramref name="name"/> the server accepts, for names from the local file system (Bulwark acceptedFileName): forbidden and control characters become "_", a reserved name or stem gets "_" after the stem, a name over the byte limit is shortened keeping its extension.</summary>
    public static string AcceptedName(string name, FileNameRules? rules)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (rules is null)
        {
            return name;
        }

        var sb = new StringBuilder();
        foreach (var r in Runes(name))
        {
            _ = sb.Append(IsControl(r) || rules.ForbiddenChars.Contains(r, StringComparison.Ordinal) ? "_" : r);
        }

        var outName = FitToBytes(sb.ToString(), rules.MaxNameBytes);
        return IsReserved(outName, rules.ForbiddenNames) ? Unreserved(outName, rules.MaxNameBytes) : outName;
    }

    private static IEnumerable<string> Runes(string text)
    {
        foreach (var r in text.EnumerateRunes())
        {
            yield return r.ToString();
        }
    }

    private static bool IsControl(string rune) => rune.Length > 0 && char.ConvertToUtf32(rune, 0) is var c && (c <= 0x1f || (c >= 0x7f && c <= 0x9f));

    private static string AsciiLower(string text) => string.Create(text.Length, text, static (span, s) =>
    {
        for (var i = 0; i < s.Length; i++)
        {
            span[i] = s[i] is >= 'A' and <= 'Z' ? (char)(s[i] + 32) : s[i];
        }
    });

    private static string Stem(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 ? name[..dot] : name;
    }

    private static bool IsReserved(string name, IReadOnlyList<string> reserved)
    {
        var whole = AsciiLower(name);
        var stem = AsciiLower(Stem(name));
        return reserved.Any(n => string.Equals(AsciiLower(n), whole, StringComparison.Ordinal) || string.Equals(AsciiLower(n), stem, StringComparison.Ordinal));
    }

    private static string CutToBytes(string text, int max)
    {
        var sb = new StringBuilder();
        var used = 0;
        foreach (var r in text.EnumerateRunes())
        {
            var n = r.Utf8SequenceLength;
            if (used + n > max)
            {
                break;
            }

            _ = sb.Append(r.ToString());
            used += n;
        }

        return sb.ToString();
    }

    private static string FitToBytes(string name, int max)
    {
        if (Encoding.UTF8.GetByteCount(name) <= max)
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var ext = dot > 0 ? name[dot..] : string.Empty;
        var extBytes = Encoding.UTF8.GetByteCount(ext);
        return ext.Length == 0 || extBytes >= max ? CutToBytes(name, max) : CutToBytes(name[..dot], max - extBytes) + ext;
    }

    private static string Unreserved(string name, int max)
    {
        var stemEnd = Stem(name).Length;
        var marked = name[..stemEnd] + "_" + name[stemEnd..];
        if (Encoding.UTF8.GetByteCount(marked) <= max)
        {
            return marked;
        }

        var stem = name[..stemEnd].EnumerateRunes().Select(r => r.ToString()).ToList();
        if (stem.Count > 0)
        {
            stem[^1] = "_";
        }

        return string.Concat(stem) + name[stemEnd..];
    }
}
