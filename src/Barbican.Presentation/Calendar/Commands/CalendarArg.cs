// The argument of a calendar menu command: parts joined by a tab (an occurrence id has a "|", a colour a "#"). Owner cal-ui.

namespace Barbican.Presentation.Calendar.Commands;

/// <summary>Joins and splits the parts of a command argument.</summary>
public static class CalendarArg
{
    private const char Separator = '\t';

    public static string Join(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(Separator, parts);
    }

    /// <summary>The parts of <paramref name="argument"/>, padded with empty strings up to <paramref name="count"/>.</summary>
    public static string[] Split(string? argument, int count)
    {
        var parts = (argument ?? string.Empty).Split(Separator);
        return parts.Length >= count ? parts : [.. parts, .. Enumerable.Repeat(string.Empty, count - parts.Length)];
    }
}
