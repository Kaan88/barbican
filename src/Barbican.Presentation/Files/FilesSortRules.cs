// Mirrors: Bulwark components/files/files-header.tsx:431-452 (the sort menu: choosing a key keeps the direction of the current key, except that a new key starts ascending for Name and descending for the others; Ascending and Descending set only the direction).

namespace Barbican.Presentation.Files;

/// <summary>What the Files sort menu does to the model.</summary>
public static class FilesSortRules
{
    /// <summary>Applies a menu choice: name, modified, size, type, asc or desc. Unknown text changes nothing.</summary>
    public static void Apply(FilesViewModel view, string? choice)
    {
        ArgumentNullException.ThrowIfNull(view);
        switch (choice)
        {
            case "asc":
                view.SortDescending = false;
                break;
            case "desc":
                view.SortDescending = true;
                break;
            case "name" or "modified" or "size" or "type":
                var key = choice switch { "name" => FilesSortKey.Name, "modified" => FilesSortKey.Modified, "size" => FilesSortKey.Size, _ => FilesSortKey.Type };
                if (view.SortKey != key)
                {
                    view.SortKey = key;
                    view.SortDescending = key is not FilesSortKey.Name and not FilesSortKey.Type;
                }

                break;
            default:
                break;
        }
    }
}
