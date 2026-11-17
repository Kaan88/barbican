// Mirrors: Rust mailto.rs:181 windows_plan, :202 desktop_quote, :214 desktop_entry, :287 windows_unplan, :322 command_matches, :364 desktop_unquote; Rust mailto.rs (tag rust-final); RFC 6068 (mailto URI); Bulwark app/[locale]/page.tsx mailto handling, lib/mailto.ts; freedesktop Desktop Entry Specification (Exec quoting), xdg-mime. Contract stub from the M3 plan section 4.5; owner composer-ui replaces the bodies.

using Barbican.Core.Common;

namespace Barbican.Core.Mailto;

/// <summary>The pure plans of the default-mail-app registration. Nothing here touches the registry or the disk; the platform services execute the plans only after the user agrees (CLAUDE.md: no OS changes without the user's OK). Stub until stream composer-ui lands.</summary>
public static class DefaultMailApp
{
    public const string ProgId = "Barbican.Url.mailto";

    /// <summary>The HKCU values that register <paramref name="exe"/> as a mail client and the mailto handler: all under <c>HKCU\Software\</c>, the open command <c>"exe" "%1"</c>, URL Protocol, URLAssociations mailto, RegisteredApplications.</summary>
    public static IReadOnlyList<RegistryWrite> WindowsPlan(string exe) => throw new PortPendingException("DefaultMailApp.WindowsPlan");

    /// <summary>The keys and names removal touches: only what <see cref="WindowsPlan"/> wrote.</summary>
    public static IReadOnlyList<(string Key, string Name)> WindowsUnplan() => throw new PortPendingException("DefaultMailApp.WindowsUnplan");

    /// <summary>The .desktop file: MimeType x-scheme-handler/mailto, an <c>Exec</c> line with the path quoted and <c>$</c> escaped, then <c>%u</c>.</summary>
    public static string DesktopEntry(string exe) => throw new PortPendingException("DefaultMailApp.DesktopEntry");

    /// <summary>The Exec value with the backslash escapes removed (the path keeps its surrounding quotes, as <see cref="CommandMatches"/> expects).</summary>
    public static string DesktopUnquote(string exec) => throw new PortPendingException("DefaultMailApp.DesktopUnquote");

    /// <summary>Whether a registered command line (<c>"exe" "%1"</c>, <c>exe %u</c>) runs <paramref name="exe"/>: false after the app moved (the path compare ignores case on Windows only).</summary>
    public static bool CommandMatches(string registered, string exe) => throw new PortPendingException("DefaultMailApp.CommandMatches");
}
