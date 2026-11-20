// Mirrors Bulwark lib/outgoing/classification-raw.ts:73-136; RFC 5322 sections 2.1-2.2.
using System.Globalization;
using System.Text;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.OrganizationPolicy;

/// <summary>Applies a verified classification policy to the outer headers without changing the MIME body.</summary>
public static class RawClassification
{
    private const int MaxHeaderBytes = 64 * 1024;

    /// <summary>Applies the default label to a direct send. Required classification needs an authorized composer flow.</summary>
    public static Result<byte[]> Apply(byte[] raw, ClassificationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Required) return Fail("A classification label is required for messages sent outside the composer.");
        if (policy.List.Count == 0) return Result<byte[]>.Ok((byte[])raw.Clone());
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sio-label", "sio-label-history", "sensitivity", "x-bulwark-label" };
        if (!string.IsNullOrEmpty(policy.ExtraHeader)) reserved.Add(policy.ExtraHeader);
        if (!TryHeaders(raw, reserved, out var retained, out var bodyStart, out var newline)) return Fail("The message has an invalid header block.");
        var label = policy.List.FirstOrDefault(item => string.Equals(item.Id, policy.DefaultId, StringComparison.Ordinal));
        var additions = label is null ? Array.Empty<byte>() : Encoding.Latin1.GetBytes(string.Join(newline, HeaderLines(label, policy.ExtraHeader, newline)) + newline);
        var ending = Encoding.ASCII.GetBytes(newline);
        var headerSize = retained.Sum(line => line.Length) + additions.Length + ending.Length;
        if (headerSize > MaxHeaderBytes) return Fail("The message header is too large.");
        var result = new byte[checked(headerSize + raw.Length - bodyStart)];
        var offset = 0;
        foreach (var (start, length) in retained)
        {
            Array.Copy(raw, start, result, offset, length);
            offset += length;
        }
        additions.CopyTo(result, offset);
        offset += additions.Length;
        ending.CopyTo(result, offset);
        offset += ending.Length;
        Array.Copy(raw, bodyStart, result, offset, raw.Length - bodyStart);
        return Result<byte[]>.Ok(result);
    }

    /// <summary>Validates an unmodified raw header block under the same bounded parsing rules.</summary>
    internal static bool HasValidHeaderBlock(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return TryHeaders(raw, [], out _, out _, out _);
    }

    private static bool TryHeaders(byte[] raw, HashSet<string> reserved, out List<(int Start, int Length)> retained, out int bodyStart, out string newline)
    {
        retained = [];
        bodyStart = 0;
        newline = string.Empty;
        var cursor = 0;
        var sawField = false;
        var skip = false;
        while (cursor < raw.Length && cursor < MaxHeaderBytes)
        {
            var end = cursor;
            while (end < raw.Length && end < MaxHeaderBytes && raw[end] != 10)
            {
                if ((raw[end] < 32 && raw[end] is not (9 or 13)) || raw[end] == 127 ||
                    (raw[end] == 13 && (end + 1 >= raw.Length || raw[end + 1] != 10))) return false;
                end++;
            }
            if (end >= raw.Length || end >= MaxHeaderBytes) return false;
            var crlf = end > cursor && raw[end - 1] == 13;
            var lineEnd = crlf ? end - 1 : end;
            var lineBreak = crlf ? "\r\n" : "\n";
            if (newline.Length == 0) newline = lineBreak;
            else if (!string.Equals(newline, lineBreak, StringComparison.Ordinal)) return false;
            if (lineEnd == cursor)
            {
                if (!sawField) return false;
                bodyStart = end + 1;
                return true;
            }
            if (raw[cursor] is 32 or 9)
            {
                if (!sawField) return false;
            }
            else
            {
                var colon = FieldColon(raw, cursor, lineEnd);
                if (colon < 0) return false;
                sawField = true;
                skip = reserved.Contains(Encoding.ASCII.GetString(raw, cursor, colon - cursor));
            }
            if (!skip) retained.Add((cursor, end + 1 - cursor));
            cursor = end + 1;
        }
        return false;
    }

    private static int FieldColon(byte[] raw, int start, int end)
    {
        var colon = start;
        while (colon < end && raw[colon] != ':')
        {
            if (raw[colon] is < 33 or > 126) return -1;
            colon++;
        }
        return colon == start || colon == end ? -1 : colon;
    }

    private static IEnumerable<string> HeaderLines(ClassificationLabel label, string extra, string newline)
    {
        var marking = (string.IsNullOrEmpty(label.Marking) ? label.Name : label.Marking).Trim().Replace("\"", "", StringComparison.Ordinal).Replace("\\", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
        var parameters = Marking(marking);
        parameters.Add("fgcolor=" + Foreground(label.Color));
        parameters.Add("bgcolor=" + label.Color);
        var value = new StringBuilder("SIO-Label:");
        var column = 11;
        for (var i = 0; i < parameters.Count; i++)
        {
            var piece = parameters[i] + (i == parameters.Count - 1 ? "" : ";");
            var fold = i > 0 && column + 1 + piece.Length > 78;
            value.Append(fold ? newline : "").Append(' ').Append(piece);
            column = fold ? 1 + piece.Length : column + 1 + piece.Length;
        }
        yield return value.ToString();
        if (!string.IsNullOrEmpty(label.Sensitivity)) yield return "Sensitivity:" + FoldValue("Sensitivity", label.Sensitivity, newline);
        var extraValue = label.ExtraHeaderValue.Trim();
        if (!string.IsNullOrEmpty(extra) && extraValue.Length != 0) yield return extra + ":" + FoldValue(extra, extraValue, newline);
    }

    private static List<string> Marking(string text)
    {
        if (text.All(c => c is >= ' ' and <= '~')) return ["marking=\"" + text + "\""];
        var encoded = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(text))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || "!#$&+-.^_`|~".Contains(c, StringComparison.Ordinal)) encoded.Append(c);
            else encoded.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }
        var value = encoded.ToString();
        if (value.Length + 15 <= 60) return ["marking*=utf-8''" + value];
        var pieces = new List<string>();
        var part = new StringBuilder();
        for (var i = 0; i < value.Length;)
        {
            var size = value[i] == '%' ? 3 : 1;
            if (part.Length + size > 45) { pieces.Add(part.ToString()); part.Clear(); }
            part.Append(value, i, size);
            i += size;
        }
        if (part.Length > 0) pieces.Add(part.ToString());
        return pieces.Select((piece, i) => i == 0 ? "marking*0*=utf-8''" + piece : $"marking*{i}*={piece}").ToList();
    }

    private static string Foreground(string color)
    {
        if (color.Length != 7 || color[0] != '#' || !int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return "#ffffff";
        static double Channel(int value) { var c = value / 255d; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        return 0.2126 * Channel((rgb >> 16) & 255) + 0.7152 * Channel((rgb >> 8) & 255) + 0.0722 * Channel(rgb & 255) > 0.179 ? "#000000" : "#ffffff";
    }

    private static string FoldValue(string name, string value, string newline)
    {
        if (value.All(c => c is >= ' ' and <= '~'))
        {
            var output = new StringBuilder();
            var column = name.Length + 1;
            foreach (var word in value.Split(' '))
            {
                var fold = output.Length > 0 && column + 1 + word.Length > 78;
                if (fold) output.Append(newline);
                output.Append(' ').Append(word);
                column = fold ? 1 + word.Length : column + 1 + word.Length;
            }
            return output.ToString();
        }
        var words = new List<string>();
        var chunk = new List<byte>();
        var firstLimit = Math.Max(3, Math.Min(45, (int)Math.Floor((78d - name.Length - 2 - 12) / 4) * 3));
        foreach (var rune in value.EnumerateRunes())
        {
            var bytes = Encoding.UTF8.GetBytes(rune.ToString());
            if (chunk.Count + bytes.Length > (words.Count == 0 ? firstLimit : 45) && chunk.Count != 0) { words.Add(Convert.ToBase64String(chunk.ToArray())); chunk.Clear(); }
            chunk.AddRange(bytes);
        }
        if (chunk.Count > 0) words.Add(Convert.ToBase64String(chunk.ToArray()));
        return " " + string.Join(newline + " ", words.Select(word => "=?UTF-8?B?" + word + "?="));
    }

    private static Result<byte[]> Fail(string message) => Result<byte[]>.Fail(new ValidationError(message));
}
