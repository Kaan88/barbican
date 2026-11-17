// Ported from Rust src/tags.rs tests (:190-:290).

using System.Text.Json.Nodes;
using Barbican.Core.Mail.Tags;

namespace Barbican.Core.Tests.Mail.Tags;

public sealed class TagRulesTests
{
    private static TagDef D(string id) => new(id, id[(id.LastIndexOf('/') + 1)..], "blue");

    [Fact]
    [RustTest("tags::ids_follow_bulwark")]
    public void Ids_follow_bulwark()
    {
        Assert.Equal("work-stuff", TagRules.NormalizeLevel("  Work Stuff!! "));
        Assert.Equal("a-b", TagRules.NormalizeLevel("a/b"));
        Assert.Equal("", TagRules.NormalizeLevel("Работа"));
        Assert.Equal("work", TagRules.ChooseId(null, "Work", []));
        Assert.Equal("work/clients", TagRules.ChooseId("work", "Clients", []));

        // taken slug or none: the generated level, then numbered
        var g = TagRules.ChooseId(null, "Работа", []);
        Assert.True(g.StartsWith("tag-", StringComparison.Ordinal) && g.Length > 4, g);
        Assert.Equal(TagRules.GeneratedLevel("Work"), TagRules.ChooseId(null, "Work", ["WORK"]));
        Assert.Equal($"{TagRules.GeneratedLevel("Work")}-2", TagRules.ChooseId(null, "Work", ["work", TagRules.GeneratedLevel("Work")]));
        Assert.True(TagRules.ValidId("work/clients"));
        Assert.False(TagRules.ValidId("a b"));
        Assert.False(TagRules.ValidId("a*b"));
        Assert.False(TagRules.ValidId(new string('x', 130)));
    }

    [Fact]
    [RustTest("tags::fnv_matches_javascript")]
    public void Fnv_matches_javascript()
    {
        // FNV-1a 32 of "a" is 0xe40c292c = 3826002220, base 36 "1r9wi7g"
        Assert.Equal("tag-1r9wi7g", TagRules.GeneratedLevel("A"));
    }

    [Fact]
    [RustTest("tags::tree_rows")]
    public void Tree_rows()
    {
        List<TagDef> defs = [D("work"), D("home"), D("work/clients"), D("work/clients/acme"), D("lost/child")];
        var flat = TagRules.Rows(defs, false, new HashSet<string>(StringComparer.Ordinal));
        Assert.Equal([0, 0, 0, 0, 0], flat.Select(r => r.Depth));

        var t = TagRules.Rows(defs, true, new HashSet<string>(StringComparer.Ordinal));
        var order = t.Select(r => (r.Def.Id, r.Depth, r.Chevron));
        Assert.Equal(
            [("work", 0, 1), ("work/clients", 1, 1), ("work/clients/acme", 2, 0), ("home", 0, 0), ("lost/child", 0, 0)],
            order);

        // an orphan is named in full
        Assert.Equal("lost/child", t[4].Def.Label);
        var closed = new HashSet<string>(["work"], StringComparer.Ordinal);
        var c = TagRules.Rows(defs, true, closed);
        Assert.Equal(["work", "home", "lost/child"], c.Select(r => r.Def.Id), StringComparer.Ordinal);
        Assert.Equal(2, c[0].Chevron);
    }

    [Fact]
    [RustTest("tags::manage_tags")]
    public void Manage_tags()
    {
        List<TagDef> defs = [D("work"), D("home")];
        Assert.True(TagRules.TryAdd(defs, "work", "Clients", out var id));
        Assert.Equal("work/clients", id);
        Assert.Equal(["work", "work/clients", "home"], defs.Select(x => x.Id), StringComparer.Ordinal);
        Assert.False(TagRules.TryAdd(defs, "work", "clients", out _));
        Assert.False(TagRules.TryAdd(defs, null, " ", out _));
        Assert.False(TagRules.TryRemove(defs, "work", out _));
        Assert.True(TagRules.TryRename(defs, "work/clients", "Customers", out _));
        Assert.Equal("Customers", defs[1].Label);
        Assert.Equal("work/clients", defs[1].Id);
        Assert.True(TagRules.TryRemove(defs, "work/clients", out _));
        Assert.True(TagRules.TryRemove(defs, "work", out _));
        Assert.Single(defs);
        TagRules.SetColor(defs, "home", "teal");
        Assert.Equal("teal", defs[0].Color);
    }

    [Fact]
    [RustTest("tags::stored_tags_and_colours")]
    public void Stored_tags_and_colours()
    {
        Assert.Equal(TagRules.Defaults(), TagRules.Parse(null));
        var v = JsonNode.Parse("""[{"id": "work", "label": "Work", "color": "green-dark"}, {"id": "bad id"}, {"id": "x"}]""")!;
        var p = TagRules.Parse(v);
        Assert.Equal(2, p.Count);
        Assert.Equal("x", p[1].Label);
        Assert.Equal(p, TagRules.Parse(TagRules.ToValue(p)));
        Assert.Equal(0xef4444u, TagRules.ColorRgb("red"));
        Assert.Equal(0x15803du, TagRules.ColorRgb("green-dark"));
        Assert.Equal(0x123abcu, TagRules.ColorRgb("#123abc"));
        Assert.Equal(0x6b7280u, TagRules.ColorRgb("nope"));
    }
}
