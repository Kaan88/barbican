// Mirrors: Rust dnd.rs:242-254 (SIDE_MIN/MAX, LIST_MIN/MAX, clamp_panes); Bulwark stores/ui-store.ts (pane sizes). Stub from stage F of M2; owner actions.

namespace Barbican.Core.Layout;

/// <summary>The width limits of the sidebar and the list pane, and the clamp applied to saved values.</summary>
public static class PaneLimits
{
    /// <summary>0 = no width saved: the pane takes the width of the current style.</summary>
    public const float SideDefault = 0f;

    /// <summary>0 = no width saved.</summary>
    public const float ListDefault = 0f;

    public const float SideMin = 180f;
    public const float SideMax = 400f;
    public const float ListMin = 240f;
    public const float ListMax = 600f;

    /// <summary>Clamps both widths; a value that is not finite or not positive becomes the default.</summary>
    public static (float Side, float List) Clamp(float side, float list)
    {
        static float Ok(float v, float lo, float hi, float d) => float.IsFinite(v) && v > 0f ? Math.Clamp(v, lo, hi) : d;
        return (Ok(side, SideMin, SideMax, SideDefault), Ok(list, ListMin, ListMax, ListDefault));
    }
}
