// Mirrors: Bulwark components/layout/navigation-rail.tsx:466 (`iconForName(page.icon) ?? Puzzle`) and components/icons iconForName. Barbican draws the icons of its own set (Tokens.Icons); a name Bulwark knows and Barbican
// has no drawing for falls back to the puzzle piece as an unknown name does there.
using Barbican.Presentation.Theme;

namespace Barbican.Presentation.Shell;

/// <summary>Maps the icon name of an add-on page to a key of the icon set.</summary>
public static class PluginPageIcons
{
    public const string Fallback = "Puzzle";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mail"] = "Mail", ["inbox"] = "Inbox", ["send"] = "Send", ["archive"] = "Archive", ["calendar"] = "Cal", ["cal"] = "Cal",
        ["users"] = "Users", ["user"] = "User", ["folder"] = "Folder", ["file"] = "File", ["filetext"] = "File", ["clock"] = "Clock",
        ["star"] = "Star", ["shield"] = "Shield", ["settings"] = "Settings", ["tag"] = "Tag", ["search"] = "Search", ["bell"] = "Bell",
        ["book"] = "Book", ["bookopen"] = "Book", ["package"] = "Package", ["wrench"] = "Tool", ["tool"] = "Tool", ["info"] = "Info",
        ["link"] = "Link", ["table"] = "Table", ["code"] = "Code", ["list"] = "List", ["lock"] = "Lock", ["key"] = "Key", ["image"] = "Image",
        ["phone"] = "Phone", ["building"] = "Building", ["eye"] = "Eye", ["globe"] = "Globe", ["sun"] = "Sun", ["sparkles"] = "Spark",
        ["trash"] = "Trash", ["paperclip"] = "Clip", ["download"] = "Download", ["puzzle"] = "Puzzle",
    };

    /// <summary>The key of <see cref="Tokens.Icons"/> for the add-on's icon name; the puzzle piece without a name or for one nobody draws.</summary>
    public static string KindFor(string? name)
    {
        if (name is null)
        {
            return Fallback;
        }

        var key = name.Replace("-", string.Empty, StringComparison.Ordinal);
        return Known.TryGetValue(key, out var kind) && Tokens.Icons.ContainsKey(kind) ? kind : Fallback;
    }
}
