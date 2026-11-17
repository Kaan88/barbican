// Mirrors: Rust tags.rs (whole); Bulwark lib/keyword-nesting.ts (buildKeywordTree, chooseKeywordId, isKeywordNameTaken, generatedKeywordLevel,
// normalizeKeywordLevel), lib/tag-hex-color.ts, settings-store emailKeywords / DEFAULT_KEYWORDS, KEYWORD_PALETTE; RFC 8621 §4.1.1. Owner mailcore.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.Core.Mail.Tags;

/// <summary>User tags: ids, nesting, colours and management.</summary>
public static class TagRules
{
    /// <summary>Separates parent from child inside a tag id.</summary>
    public const char Separator = '/';

    /// <summary>The longest keyword Stalwart keeps (bytes); the id gets what is left after <c>$label:</c>.</summary>
    public const int MaxKeyword = 128;

    private const string Prefix = "$label:";
    private const string ForbiddenInKeyword = "(){]%*\"\\";

    /// <summary>The base palette offered in the colour menu.</summary>
    public static IReadOnlyList<string> Palette { get; } =
        ["red", "orange", "amber", "yellow", "lime", "green", "teal", "cyan", "blue", "indigo", "purple", "pink", "gray"];

    /// <summary>The seven colour tags a new account starts with (Bulwark DEFAULT_KEYWORDS).</summary>
    public static IReadOnlyList<TagDef> Defaults() =>
        [.. new[] { ("red", "Red"), ("orange", "Orange"), ("yellow", "Yellow"), ("green", "Green"), ("blue", "Blue"), ("purple", "Purple"), ("pink", "Pink") }
            .Select(t => new TagDef(t.Item1, t.Item2, t.Item1))];

    /// <summary>Tag definitions from stored settings (the defaults until the user changes them).</summary>
    public static IReadOnlyList<TagDef> Parse(JsonNode? stored)
    {
        if (stored is not JsonArray array)
        {
            return Defaults();
        }

        var result = new List<TagDef>();
        foreach (var node in array)
        {
            var id = StringOf(node, "id")?.Trim();
            if (id is null || !ValidId(id))
            {
                continue;
            }

            var label = StringOf(node, "label");
            var color = StringOf(node, "color");
            result.Add(new TagDef(id, string.IsNullOrWhiteSpace(label) ? id : label, color ?? "gray"));
        }

        return result;
    }

    /// <summary>The stored form: an array of <c>{id, label, color}</c>.</summary>
    public static JsonArray ToValue(IReadOnlyList<TagDef> defs)
    {
        ArgumentNullException.ThrowIfNull(defs);
        var array = new JsonArray();
        foreach (var d in defs)
        {
            JsonNode node = new JsonObject { ["id"] = d.Id, ["label"] = d.Label, ["color"] = d.Color };
            array.Add(node);
        }

        return array;
    }

    /// <summary>RFC 8621 keyword syntax for the whole <c>$label:id</c>, within Stalwart's length.</summary>
    public static bool ValidId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var kw = Prefix + id;
        return id.Length > 0
            && Encoding.UTF8.GetByteCount(kw) <= MaxKeyword
            && kw.All(c => c is >= '!' and <= '~' && !ForbiddenInKeyword.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>One level of an id from a name: lowercase, anything outside [a-z0-9_-] folded to one dash (Bulwark normalizeKeywordLevel).</summary>
    public static string NormalizeLevel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var sb = new StringBuilder();
        foreach (var raw in name.Trim().ToLowerInvariant())
        {
            var c = raw is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? raw : '-';
            if (c == '-' && sb.Length > 0 && sb[^1] == '-')
            {
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim('-');
    }

    /// <summary><c>tag-</c> and a 32-bit FNV-1a hash of the name's UTF-16 units in base 36 (Bulwark generatedKeywordLevel).</summary>
    public static string GeneratedLevel(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var h = 0x811c9dc5u;
        foreach (var unit in name.Trim().ToLowerInvariant())
        {
            h = unchecked((h ^ unit) * 0x01000193u);
        }

        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var n = (ulong)h;
        var chars = new List<char>();
        do
        {
            chars.Add(digits[(int)(n % 36)]);
            n /= 36;
        }
        while (n != 0);
        chars.Reverse();
        return "tag-" + new string([.. chars]);
    }

    public static string? ParentOf(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var i = id.LastIndexOf(Separator);
        return i < 0 ? null : id[..i];
    }

    public static bool IsDescendant(string id, string ancestor)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(ancestor);
        return id.StartsWith(ancestor + Separator, StringComparison.Ordinal);
    }

    /// <summary>The id a new tag called <paramref name="name"/> gets under <paramref name="parent"/> (Bulwark chooseKeywordId).</summary>
    public static string ChooseId(string? parent, string name, IReadOnlyList<string> taken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(taken);
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var takenSet = new HashSet<string>(taken.Select(t => t.ToLowerInvariant()), StringComparer.Ordinal);
        string Join(string part) => string.IsNullOrEmpty(parent) ? part : parent + Separator + part;
        var slug = NormalizeLevel(name);
        if (slug.Length > 0 && !takenSet.Contains(Join(slug)))
        {
            return Join(slug);
        }

        var level = GeneratedLevel(name);
        var id = Join(level);
        var n = 2;
        while (takenSet.Contains(id))
        {
            id = Join(level + "-" + n.ToString(CultureInfo.InvariantCulture));
            n++;
        }

        return id;
    }

    /// <summary>Whether a tag directly under <paramref name="parent"/> already has this name (Bulwark isKeywordNameTaken).</summary>
    public static bool NameTaken(IReadOnlyList<TagDef> defs, string? parent, string name, string? except)
    {
        ArgumentNullException.ThrowIfNull(defs);
        ArgumentNullException.ThrowIfNull(name);
        var want = name.Trim().ToLowerInvariant();
        return want.Length > 0 && defs.Any(d =>
            !string.Equals(d.Id, except, StringComparison.Ordinal)
            && string.Equals(ParentOf(d.Id), parent, StringComparison.Ordinal)
            && string.Equals(d.Label.Trim().ToLowerInvariant(), want, StringComparison.Ordinal));
    }

    /// <summary>The tags in sidebar order (Bulwark buildKeywordTree); children of a collapsed tag are left out.</summary>
    public static IReadOnlyList<TagRow> Rows(IReadOnlyList<TagDef> defs, bool nested, ISet<string> collapsed)
    {
        ArgumentNullException.ThrowIfNull(defs);
        ArgumentNullException.ThrowIfNull(collapsed);
        if (!nested)
        {
            return [.. defs.Select(d => new TagRow(d, 0, 0))];
        }

        var ids = new HashSet<string>(defs.Select(d => d.Id), StringComparer.Ordinal);
        var output = new List<TagRow>();
        foreach (var d in defs)
        {
            var parent = ParentOf(d.Id);
            if (parent is null || !ids.Contains(parent))
            {
                Walk(d, 0, defs, collapsed, output, parent is not null);
            }
        }

        return output;
    }

    /// <summary>Tag palette (Bulwark KEYWORD_PALETTE, Tailwind shades) or a custom <c>#rrggbb</c>, as 0xRRGGBB.</summary>
    public static uint ColorRgb(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.StartsWith('#') && name.Length == 7 && uint.TryParse(name.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var custom))
        {
            return custom;
        }

        var hue = name;
        var shade = 1;
        var dash = name.LastIndexOf('-');
        if (dash >= 0)
        {
            var suffix = name[(dash + 1)..];
            if (string.Equals(suffix, "light", StringComparison.Ordinal))
            {
                hue = name[..dash];
                shade = 0;
            }
            else if (string.Equals(suffix, "dark", StringComparison.Ordinal))
            {
                hue = name[..dash];
                shade = 2;
            }
        }

        uint[] s = hue switch
        {
            "red" => [0xfca5a5, 0xef4444, 0xb91c1c],
            "orange" => [0xfdba74, 0xf97316, 0xc2410c],
            "amber" => [0xfcd34d, 0xf59e0b, 0xb45309],
            "yellow" => [0xfde047, 0xeab308, 0xa16207],
            "lime" => [0xbef264, 0x84cc16, 0x4d7c0f],
            "green" => [0x86efac, 0x22c55e, 0x15803d],
            "teal" => [0x5eead4, 0x14b8a6, 0x0f766e],
            "cyan" => [0x67e8f9, 0x06b6d4, 0x0e7490],
            "blue" => [0x93c5fd, 0x3b82f6, 0x1d4ed8],
            "indigo" => [0xa5b4fc, 0x6366f1, 0x4338ca],
            "purple" => [0xd8b4fe, 0xa855f7, 0x7e22ce],
            "pink" => [0xf9a8d4, 0xec4899, 0xbe185d],
            _ => [0xd1d5db, 0x6b7280, 0x374151],
        };
        return s[shade];
    }

    /// <summary>Adds a tag (under <paramref name="parent"/> when nesting); on success <paramref name="idOrError"/> is the new id, else why not.</summary>
    public static bool TryAdd(IList<TagDef> defs, string? parent, string name, out string idOrError)
    {
        ArgumentNullException.ThrowIfNull(defs);
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        var list = defs.ToList();
        if (name.Length == 0)
        {
            idOrError = "Give the tag a name.";
            return false;
        }

        if (NameTaken(list, parent, name, null))
        {
            idOrError = "A tag with that name is already there.";
            return false;
        }

        var id = ChooseId(parent, name, [.. list.Select(d => d.Id)]);
        if (!ValidId(id))
        {
            idOrError = "That name nests too deep for a tag.";
            return false;
        }

        // a child goes after its parent's last descendant, a root at the end
        var color = (parent is null ? null : list.Find(d => string.Equals(d.Id, parent, StringComparison.Ordinal))?.Color) ?? "blue";
        var at = list.Count;
        if (parent is not null)
        {
            var last = list.FindLastIndex(d => string.Equals(d.Id, parent, StringComparison.Ordinal) || IsDescendant(d.Id, parent));
            if (last >= 0)
            {
                at = last + 1;
            }
        }

        defs.Insert(at, new TagDef(id, name, color));
        idOrError = id;
        return true;
    }

    /// <summary>Renames a tag's label (its id, and so the messages' keyword, stays).</summary>
    public static bool TryRename(IList<TagDef> defs, string id, string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(defs);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        name = name.Trim();
        error = string.Empty;
        if (name.Length == 0)
        {
            error = "Give the tag a name.";
            return false;
        }

        if (NameTaken([.. defs], ParentOf(id), name, id))
        {
            error = "A tag with that name is already there.";
            return false;
        }

        var i = IndexOf(defs, id);
        if (i < 0)
        {
            error = "That tag is gone.";
            return false;
        }

        defs[i] = defs[i] with { Label = name };
        return true;
    }

    /// <summary>Removes a tag definition; a tag with tags under it stays until those are gone.</summary>
    public static bool TryRemove(IList<TagDef> defs, string id, out string error)
    {
        ArgumentNullException.ThrowIfNull(defs);
        ArgumentNullException.ThrowIfNull(id);
        error = string.Empty;
        if (defs.Any(d => IsDescendant(d.Id, id)))
        {
            error = "Delete the tags inside it first.";
            return false;
        }

        var i = IndexOf(defs, id);
        if (i < 0)
        {
            error = "That tag is gone.";
            return false;
        }

        defs.RemoveAt(i);
        return true;
    }

    public static void SetColor(IList<TagDef> defs, string id, string color)
    {
        ArgumentNullException.ThrowIfNull(defs);
        var i = IndexOf(defs, id);
        if (i >= 0)
        {
            defs[i] = defs[i] with { Color = color };
        }
    }

    private static int IndexOf(IList<TagDef> defs, string id)
    {
        for (var i = 0; i < defs.Count; i++)
        {
            if (string.Equals(defs[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string? StringOf(JsonNode? node, string key) =>
        node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static void Walk(TagDef d, int depth, IReadOnlyList<TagDef> defs, ISet<string> collapsed, List<TagRow> output, bool orphan)
    {
        var kids = defs.Where(k => string.Equals(ParentOf(k.Id), d.Id, StringComparison.Ordinal)).ToList();
        var closed = collapsed.Contains(d.Id);
        var def = orphan ? d with { Label = ParentOf(d.Id) + Separator + d.Label } : d;
        output.Add(new TagRow(def, depth, kids.Count == 0 ? 0 : closed ? 2 : 1));
        if (!closed)
        {
            foreach (var k in kids)
            {
                Walk(k, depth + 1, defs, collapsed, output, false);
            }
        }
    }
}
