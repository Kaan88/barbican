using System.Globalization;

namespace Barbican.Automation.Protocol;

/// <summary>
/// One request line of the automation protocol (spec/automation-protocol.md): a command name, a
/// space, the rest. UI-free so tests/Barbican.Automation.Tests covers it.
/// </summary>
public sealed record CommandLine(string Name, string Argument)
{
    /// <summary>Longest request line the server reads; longer lines are refused unread.</summary>
    public const int MaxLength = 64 * 1024;

    public IReadOnlyList<string> Tokens { get; } = Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public static CommandLine Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var trimmed = line.TrimEnd('\r', '\n');
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space < 0
            ? new CommandLine(trimmed, string.Empty)
            : new CommandLine(trimmed[..space], trimmed[(space + 1)..]);
    }

    /// <summary>The i-th token as a number (invariant culture), or <paramref name="fallback"/>.</summary>
    public double Number(int index, double fallback = 0) =>
        index < Tokens.Count && double.TryParse(Tokens[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>The i-th token, or <paramref name="fallback"/>.</summary>
    public string Token(int index, string fallback = "") => index < Tokens.Count ? Tokens[index] : fallback;

    /// <summary>A reply on one line: CR and LF become spaces (the protocol is one line out per line in).</summary>
    public static string OneLine(string reply) =>
        (reply ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
}
