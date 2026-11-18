// Mirrors: M4 CalendarArg (a command takes one string argument; the menus pack several values into it). Owner con-ui.

namespace Barbican.Presentation.Contacts;

/// <summary>Packs and unpacks the argument of a contacts command: values separated by the unit separator (U+001F), which no id, category or name carries.</summary>
public static class ContactsArg
{
    private const char Separator = '\u001F';

    public static string Join(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(Separator, parts);
    }

    public static string Join(string first, IEnumerable<string> rest)
    {
        ArgumentNullException.ThrowIfNull(rest);
        return string.Join(Separator, new[] { first }.Concat(rest));
    }

    /// <summary>All the values, none for a null or empty argument.</summary>
    public static IReadOnlyList<string> Split(string? argument) => string.IsNullOrEmpty(argument) ? [] : argument.Split(Separator);
}
