// Mirrors: Rust mailto.rs:181 windows_plan, :202 desktop_quote, :214 desktop_entry, :287 windows_unplan, :322 command_matches, :364 desktop_unquote; RFC 6068; freedesktop Desktop Entry Specification (Exec quoting), xdg-mime; Windows default-programs registration. Port by M3 composer-ui.

using System.Text;

namespace Barbican.Core.Mailto;

/// <summary>The pure plans of the default-mail-app registration. Nothing here touches the registry or the disk; the platform services execute the plans only after the user agrees (CLAUDE.md: no OS changes without the user's OK).</summary>
public static class DefaultMailApp
{
    public const string ProgId = "Barbican.Url.mailto";

    private const string BaseKey = @"HKCU\Software\Clients\Mail\Barbican";

    private const string ClassKey = @"HKCU\Software\Classes\" + ProgId;

    /// <summary>The HKCU values that register <paramref name="exe"/> as a mail client and the mailto handler: all under <c>HKCU\Software\</c>, the open command <c>"exe" "%1"</c>, URL Protocol, URLAssociations mailto, RegisteredApplications.</summary>
    public static IReadOnlyList<RegistryWrite> WindowsPlan(string exe)
    {
        ArgumentNullException.ThrowIfNull(exe);
        return
        [
            new(ClassKey, "", "Barbican mailto link"),
            new(ClassKey, "URL Protocol", ""),
            new(ClassKey + @"\DefaultIcon", "", $"\"{exe}\",0"),
            new(ClassKey + @"\shell\open\command", "", $"\"{exe}\" \"%1\""),
            new(BaseKey, "", "Barbican"),
            new(BaseKey + @"\Capabilities", "ApplicationName", "Barbican"),
            new(BaseKey + @"\Capabilities", "ApplicationDescription", "Mail, calendar and contacts for Stalwart"),
            new(BaseKey + @"\Capabilities\URLAssociations", "mailto", ProgId),
            new(BaseKey + @"\shell\open\command", "", $"\"{exe}\""),
            new(@"HKCU\Software\RegisteredApplications", "Barbican", @"Software\Clients\Mail\Barbican\Capabilities"),
        ];
    }

    /// <summary>The keys and names removal touches: only what <see cref="WindowsPlan"/> wrote.</summary>
    public static IReadOnlyList<(string Key, string Name)> WindowsUnplan() =>
    [
        (ClassKey, ""),
        (BaseKey, ""),
        (@"HKCU\Software\RegisteredApplications", "Barbican"),
    ];

    /// <summary>The .desktop file: MimeType x-scheme-handler/mailto, an <c>Exec</c> line with the path quoted and <c>$</c> escaped, then <c>%u</c>.</summary>
    public static string DesktopEntry(string exe)
    {
        ArgumentNullException.ThrowIfNull(exe);
        return "[Desktop Entry]\nType=Application\nName=Barbican\nComment=Mail, calendar and contacts for Stalwart\nExec=" + DesktopQuote(exe) + " %u\nIcon=barbican\nTerminal=false\nCategories=Network;Email;\nMimeType=x-scheme-handler/mailto;\nStartupNotify=true\n";
    }

    /// <summary>The Exec value with the backslash escapes removed (the path keeps its surrounding quotes, as <see cref="CommandMatches"/> expects).</summary>
    public static string DesktopUnquote(string exec)
    {
        ArgumentNullException.ThrowIfNull(exec);
        var sb = new StringBuilder();
        for (var i = 0; i < exec.Length; i++)
        {
            if (exec[i] == '\\')
            {
                if (i + 1 < exec.Length)
                {
                    sb.Append(exec[++i]);
                }
            }
            else
            {
                sb.Append(exec[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>Whether a registered command line (<c>"exe" "%1"</c>, <c>exe %u</c>) runs <paramref name="exe"/>: false after the app moved (the path compare ignores case on Windows only).</summary>
    public static bool CommandMatches(string registered, string exe)
    {
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(exe);
        var t = registered.Trim();
        string path;
        if (t.StartsWith('"'))
        {
            var rest = t[1..];
            var end = rest.IndexOf('"', StringComparison.Ordinal);
            path = end >= 0 ? rest[..end] : rest;
        }
        else
        {
            path = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        return string.Equals(path, exe, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string DesktopQuote(string p)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in p)
        {
            if (c is '"' or '`' or '$' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.Append('"').ToString();
    }
}
