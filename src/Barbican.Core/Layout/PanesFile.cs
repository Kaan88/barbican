// Mirrors: Rust dnd.rs:256-264 load_panes / save_panes: panes.json is {"sidebar": n, "list": n} in the profile directory, widths rounded, clamped on load
// (clamp_panes) and on save. Read once at start; a missing or broken file is the defaults.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Barbican.Core.Layout;

/// <summary>The saved widths of the sidebar and the list pane.</summary>
public static class PanesFile
{
    /// <summary>The file name inside the profile directory.</summary>
    public const string FileName = "panes.json";

    /// <summary>Reads the widths; 0 means "no width saved" (the style decides).</summary>
    public static (float Side, float List) Load(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
            {
                return PaneLimits.Clamp(Read(o, "sidebar"), Read(o, "list"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // no file or an unreadable one: the defaults
        }

        return (PaneLimits.SideDefault, PaneLimits.ListDefault);
    }

    /// <summary>Reads the list height of the stacked mail layout (SH-12); 0 means none saved.</summary>
    public static float LoadHeight(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
            {
                return PaneLimits.ClampHeight(Read(o, "listHeight"));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // no file or an unreadable one: none saved
        }

        return PaneLimits.ListHeightDefault;
    }

    /// <summary>Writes the widths (clamped, rounded) and the stacked layout's list height when one is saved; a write that fails is ignored, as in Rust.</summary>
    public static void Save(string path, float side, float list, float listHeight = 0f)
    {
        var (s, l) = PaneLimits.Clamp(side, list);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var o = new JsonObject { ["sidebar"] = Math.Round(s, MidpointRounding.AwayFromZero), ["list"] = Math.Round(l, MidpointRounding.AwayFromZero) };
            if (PaneLimits.ClampHeight(listHeight) is > 0f and var h)
            {
                o["listHeight"] = Math.Round(h, MidpointRounding.AwayFromZero);
            }

            File.WriteAllText(path, o.ToJsonString());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a read-only profile: the widths are not kept
        }
    }

    private static float Read(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? (float)d : 0f;
}
