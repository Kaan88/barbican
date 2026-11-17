// Mirrors: Bulwark lib/keyword-nesting.ts (add, rename, isKeywordNameTaken, chooseKeywordId), stores/settings-store.ts emailKeywords (the tag definitions live in the settings; a
// message keeps its $label:id keyword when the definition is deleted), components/layout/sidebar.tsx (tag row menu); Rust tags.rs. The rules are Core's TagRules; this saves the
// result. Spec I-055..I-058, MENU-TAG. Owner actions (m2-close-a).

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Tags;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Tags;

/// <summary>Creates, renames, recolours and deletes tag definitions in the settings (<c>emailKeywords</c>).</summary>
public sealed class TagService(IPrefsStore prefs, IPrefsEditor editor)
{
    private const string Key = "emailKeywords";

    /// <summary>The definitions now.</summary>
    public IReadOnlyList<TagDef> Defs => TagRules.Parse(prefs.Current.Values[Key]);

    /// <summary>The label of a tag, or its id.</summary>
    public string LabelOf(string id) => Defs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal))?.Label ?? id;

    /// <summary>The colour name of a tag.</summary>
    public string? ColourOf(string id) => Defs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal))?.Color;

    /// <summary>Adds a tag, under <paramref name="parent"/> when the setting nests tags.</summary>
    public Result<string> Add(string? parent, string name)
    {
        var defs = Defs.ToList();
        if (!TagRules.TryAdd(defs, parent, name, out var idOrError))
        {
            return Result<string>.Fail(new ValidationError(idOrError));
        }

        Save(defs);
        return Result<string>.Ok(idOrError);
    }

    /// <summary>Renames the label of a tag; the id, and so the keyword on the messages, stays.</summary>
    public Result<Unit> Rename(string id, string name)
    {
        var defs = Defs.ToList();
        if (!TagRules.TryRename(defs, id, name, out var error))
        {
            return Result<Unit>.Fail(new ValidationError(error));
        }

        Save(defs);
        return Result<Unit>.Ok(default);
    }

    /// <summary>Sets a colour of the palette.</summary>
    public Result<Unit> SetColour(string id, string colour)
    {
        if (!TagRules.Palette.Contains(colour, StringComparer.Ordinal))
        {
            return Result<Unit>.Fail(new ValidationError("That is not a tag colour."));
        }

        var defs = Defs.ToList();
        TagRules.SetColor(defs, id, colour);
        Save(defs);
        return Result<Unit>.Ok(default);
    }

    /// <summary>Deletes the definition; messages keep the keyword (Bulwark; the confirmation says so). A tag with tags inside is refused.</summary>
    public Result<Unit> Delete(string id)
    {
        var defs = Defs.ToList();
        if (!TagRules.TryRemove(defs, id, out var error))
        {
            return Result<Unit>.Fail(new ValidationError(error));
        }

        Save(defs);
        return Result<Unit>.Ok(default);
    }

    private void Save(IReadOnlyList<TagDef> defs) => editor.Put(Key, TagRules.ToValue(defs));
}
