// Mirrors: M3 plan 6.3 step 3 (the editor's context menu runs commands through the M2 command registry); Rust ctxmenu.rs picks :365.

using System.Text;

namespace Barbican.Presentation.Editor;

/// <summary>The command ids of the editor (<c>rte.bold</c>, <c>rte.table-row-above</c>, ...): one per <see cref="EditorCommand"/>.</summary>
public static class EditorCommandIds
{
    public const string Prefix = "rte.";

    public static string For(EditorCommand command)
    {
        var name = command.ToString();
        var sb = new StringBuilder(Prefix);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }

    public static EditorCommand? Parse(string id) => Enum.GetValues<EditorCommand>().Select(c => ((EditorCommand?)c)).FirstOrDefault(c => string.Equals(For(c!.Value), id, StringComparison.Ordinal));
}
