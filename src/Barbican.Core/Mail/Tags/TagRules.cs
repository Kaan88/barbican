// Mirrors: Rust tags.rs (whole); Bulwark lib/keyword-nesting.ts, settings-store emailKeywords; RFC 8621 §4.1.1. Stub from F2; owner mailcore.

using Barbican.Core.Common;
using System.Text.Json.Nodes;

namespace Barbican.Core.Mail.Tags;

/// <summary>User tags: ids, nesting, colours and management.</summary>
public static class TagRules
{
    /// <summary>Separates parent from child inside a tag id.</summary>
    public const char Separator = '/';

    /// <summary>The longest keyword Stalwart keeps (bytes); the id gets what is left after <c>$label:</c>.</summary>
    public const int MaxKeyword = 128;

    /// <summary>The base palette offered in the colour menu.</summary>
    public static IReadOnlyList<string> Palette { get; } =
        ["red", "orange", "amber", "yellow", "lime", "green", "teal", "cyan", "blue", "indigo", "purple", "pink", "gray"];

    /// <summary>The seven colour tags a new account starts with (Bulwark DEFAULT_KEYWORDS).</summary>
    public static IReadOnlyList<TagDef> Defaults() => throw new PortPendingException("TagRules.Defaults: mailcore stream");

    /// <summary>Tag definitions from stored settings (the defaults until the user changes them).</summary>
    public static IReadOnlyList<TagDef> Parse(JsonNode? stored) => throw new PortPendingException("TagRules.Parse: mailcore stream");

    /// <summary>The stored form: an array of <c>{id, label, color}</c>.</summary>
    public static JsonArray ToValue(IReadOnlyList<TagDef> defs) => throw new PortPendingException("TagRules.ToValue: mailcore stream");

    /// <summary>RFC 8621 keyword syntax for the whole <c>$label:id</c>, within Stalwart's length.</summary>
    public static bool ValidId(string id) => throw new PortPendingException("TagRules.ValidId: mailcore stream");

    /// <summary>One level of an id from a name: lowercase, anything outside [a-z0-9_-] folded to one dash (Bulwark normalizeKeywordLevel).</summary>
    public static string NormalizeLevel(string name) => throw new PortPendingException("TagRules.NormalizeLevel: mailcore stream");

    /// <summary><c>tag-</c> and a 32-bit FNV-1a hash of the name's UTF-16 units in base 36 (Bulwark generatedKeywordLevel).</summary>
    public static string GeneratedLevel(string name) => throw new PortPendingException("TagRules.GeneratedLevel: mailcore stream");

    public static string? ParentOf(string id) => throw new PortPendingException("TagRules.ParentOf: mailcore stream");

    public static bool IsDescendant(string id, string ancestor) => throw new PortPendingException("TagRules.IsDescendant: mailcore stream");

    /// <summary>The id a new tag called <paramref name="name"/> gets under <paramref name="parent"/> (Bulwark chooseKeywordId).</summary>
    public static string ChooseId(string? parent, string name, IReadOnlyList<string> taken) => throw new PortPendingException("TagRules.ChooseId: mailcore stream");

    /// <summary>Whether a tag directly under <paramref name="parent"/> already has this name (Bulwark isKeywordNameTaken).</summary>
    public static bool NameTaken(IReadOnlyList<TagDef> defs, string? parent, string name, string? except) =>
        throw new PortPendingException("TagRules.NameTaken: mailcore stream");

    /// <summary>The tags in sidebar order (Bulwark buildKeywordTree); children of a collapsed tag are left out.</summary>
    public static IReadOnlyList<TagRow> Rows(IReadOnlyList<TagDef> defs, bool nested, ISet<string> collapsed) =>
        throw new PortPendingException("TagRules.Rows: mailcore stream");

    /// <summary>Tag palette (Bulwark KEYWORD_PALETTE, Tailwind shades) or a custom <c>#rrggbb</c>, as 0xRRGGBB.</summary>
    public static uint ColorRgb(string name) => throw new PortPendingException("TagRules.ColorRgb: mailcore stream");

    /// <summary>Adds a tag (under <paramref name="parent"/> when nesting); on success <paramref name="idOrError"/> is the new id, else why not.</summary>
    public static bool TryAdd(IList<TagDef> defs, string? parent, string name, out string idOrError) =>
        throw new PortPendingException("TagRules.TryAdd: mailcore stream");

    /// <summary>Renames a tag's label (its id, and so the messages' keyword, stays).</summary>
    public static bool TryRename(IList<TagDef> defs, string id, string name, out string error) =>
        throw new PortPendingException("TagRules.TryRename: mailcore stream");

    /// <summary>Removes a tag definition; a tag with tags under it stays until those are gone.</summary>
    public static bool TryRemove(IList<TagDef> defs, string id, out string error) => throw new PortPendingException("TagRules.TryRemove: mailcore stream");

    public static void SetColor(IList<TagDef> defs, string id, string color) => throw new PortPendingException("TagRules.SetColor: mailcore stream");
}
