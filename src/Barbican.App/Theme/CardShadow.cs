// Mirrors: Bulwark depth cues (docs/milestones/bulwark-parity-gaps.md TH-06): dialog card shadow-xl (components/ui/dialog.tsx:181), menu panel shadow-lg
// (components/ui/context-menu.tsx:667), toast shadow-lg (components/ui/toast.tsx:92-107), command palette shadow-xl (components/command-palette.tsx:336).
// The shared place that gives a Bulwark floating card its depth: a ThemeShadow lifted on the Z axis, cast on the layer behind the card. Bento and Nordic draw
// their own surfaces without one. Every card stays in-window (ADR 0007); the shadow is composition, it never opens a popup.

#if WINDOWS
using System.Numerics;
#endif

namespace Barbican.App.Theme;

/// <summary>Lifts a card with the theme shadow in the Bulwark style.</summary>
public static class CardShadow
{
    /// <summary>The Z lift in pixels of a depth.</summary>
    public static float Lift(CardDepth depth) => depth == CardDepth.ExtraLarge ? 32f : 20f;

    /// <summary>
    /// Gives <paramref name="card"/> the Bulwark shadow, or removes it in Bento and Nordic. <paramref name="receiver"/> is the element behind it that the shadow falls on
    /// (a card in a popup falls on the popup's own layer when none is given).
    /// </summary>
    public static void Apply(UIElement card, CardDepth depth, UIElement? receiver = null)
    {
#if WINDOWS
        if (!StyleState.Current.IsBulwark)
        {
            card.Shadow = null;
            card.Translation = Vector3.Zero;
            return;
        }

        var shadow = new ThemeShadow();
        if (receiver is not null)
        {
            shadow.Receivers.Add(receiver);
        }

        card.Shadow = shadow;
        card.Translation = new Vector3(0, 0, Lift(depth));
#else
        // Uno Skia: ThemeShadow is not drawn there yet; the card keeps its border (the depth cue is a Windows nicety, not behaviour)
        _ = (card, depth, receiver);
#endif
    }
}
