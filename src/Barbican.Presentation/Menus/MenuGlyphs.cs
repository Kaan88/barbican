// Mirrors: Rust ctxmenu.rs :56-:75 (the icon of each row), as keys of the app's line-icon set (Tokens.Icons, SIcon) instead of Segoe Fluent code points, which the Linux head does not have. Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>The icons of the menus: keys of <c>Tokens.Icons</c>.</summary>
public static class MenuGlyphs
{
    public const string Reply = "Reply";
    public const string ReplyAll = "ReplyAll";
    public const string Forward = "Fwd";
    public const string Archive = "Archive";
    public const string Delete = "Trash";
    public const string Move = "Move";
    public const string Restore = "Undo";
    public const string Inbox = "Inbox";
    public const string Read = "Check";
    public const string Unread = "Unread";
    public const string Star = "Star";
    public const string Starred = "Star";
    public const string Tag = "Tag";
    public const string Spam = "Alert";
    public const string More = "More";
    public const string Open = "External";
    public const string Print = "Print";
    public const string Download = "Download";
    public const string Copy = "Clip";
    public const string Search = "Search";
    public const string Folder = "Folder";
    public const string Refresh = "Refresh";
    public const string NewFolder = "Folder";
    public const string Rename = "Pen";
    public const string Upload = "Send";
    public const string Add = "Plus";
    public const string Cancel = "Close";
    public const string Select = "Check";
    public const string Contact = "User";
    public const string SignOut = "Back";
    public const string Default = "Star";

    /// <summary>The icon text of a coloured dot (a tag colour); the host draws a dot of that colour.</summary>
    public static string Dot(uint rgb) => "dot:" + (rgb & 0xFFFFFF).ToString("X6", System.Globalization.CultureInfo.InvariantCulture);
}
