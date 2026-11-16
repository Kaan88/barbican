using System.Text.RegularExpressions;

namespace Barbican.GenTokens;

/// <summary>
/// A deliberately small reader for theme.slint: it takes the top-level `property` lines of each
/// `export global` and skips everything else (functions, components). It is not a Slint parser.
/// </summary>
public static partial class SlintTheme
{
    [GeneratedRegex(@"^\s*export\s+global\s+(?<name>[A-Za-z_][\w-]*)\s*\{\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GlobalStart();

    [GeneratedRegex(@"^\s*((?<mode>in-out|in|out)\s+)?property\s*<\s*(?<type>[^>]+?)\s*>\s*(?<name>[A-Za-z_][\w-]*)\s*(:\s*(?<expr>.*?))?\s*;\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PropertyLine();

    public static IReadOnlyList<SlintGlobal> Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var globals = new List<SlintGlobal>();
        string? current = null;
        List<SlintProperty>? props = null;
        var depth = 0;
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = StripComment(lines[i]);
            if (current is null)
            {
                var start = GlobalStart().Match(line);
                if (start.Success)
                {
                    current = start.Groups["name"].Value;
                    props = [];
                    depth = 1;
                }

                continue;
            }

            if (depth == 1)
            {
                var m = PropertyLine().Match(line);
                if (m.Success)
                {
                    var mode = m.Groups["mode"].Value;
                    var expr = m.Groups["expr"].Success && m.Groups["expr"].Value.Length > 0 ? m.Groups["expr"].Value : null;
                    props!.Add(new SlintProperty(m.Groups["name"].Value, m.Groups["type"].Value, expr, mode is "in" or "in-out", i + 1));
                    continue;
                }
            }

            depth += CountBraces(line);
            if (depth == 0)
            {
                globals.Add(new SlintGlobal(current, props!));
                current = null;
                props = null;
            }
        }

        if (current is not null)
        {
            throw new FormatException($"Unterminated global '{current}'");
        }

        return globals;
    }

    /// <summary>Removes a trailing `//` comment that is not inside a string literal.</summary>
    internal static string StripComment(string line)
    {
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                return line[..i];
            }
        }

        return line;
    }

    private static int CountBraces(string line)
    {
        var n = 0;
        var inString = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && (i == 0 || line[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && c == '{')
            {
                n++;
            }
            else if (!inString && c == '}')
            {
                n--;
            }
        }

        return n;
    }
}
