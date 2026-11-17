// Mirrors: Rust ctxmenu.rs :56-:75 (the Segoe Fluent Icons code points the menus use). Owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>The icon glyphs of the menus (Segoe Fluent Icons).</summary>
public static class MenuGlyphs
{
    public const string Reply = "";
    public const string ReplyAll = "";
    public const string Forward = "";
    public const string Archive = "";
    public const string Delete = "";
    public const string Move = "";
    public const string Read = "";
    public const string Unread = "";
    public const string Star = "";
    public const string Starred = "";
    public const string Tag = "";
    public const string Spam = "";
    public const string More = "";
    public const string Open = "";
    public const string Print = "";
    public const string Download = "";
    public const string Copy = "";
    public const string Search = "";
    public const string Folder = "";
    public const string Refresh = "";
    public const string NewFolder = "";
    public const string Rename = "";
    public const string Upload = "";
    public const string Add = "";
    public const string Cancel = "";
    public const string Select = "";
    public const string Contact = "";
    public const string SignOut = "";
    public const string Default = "";

    /// <summary>The icon text of a coloured dot (a tag colour); the host draws a dot of that colour.</summary>
    public static string Dot(uint rgb) => "dot:" + (rgb & 0xFFFFFF).ToString("X6", System.Globalization.CultureInfo.InvariantCulture);
}
