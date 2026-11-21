// Mirrors: Bulwark ui/button.tsx:42 (rounded-md buttons), email-composer.tsx:7505 (chips), :6933 (suggestion list), avatars and remove buttons rounded-full.
// Composer corner radii from StyleState: Bulwark reads the shared Shape tokens (ButtonRadius), Bento and Nordic keep their rounder look. Circles are half the side.

using Barbican.App.Shell;
using Barbican.App.Theme;

namespace Barbican.App.Views.Compose;

/// <summary>Corner radii of the composer's code-built controls.</summary>
internal static class ComposerShape
{
    private static StyleState Look => ViewHost.Get<StyleState>();

    /// <summary>A button or chip: the shared button radius in Bulwark, a pill of this height elsewhere.</summary>
    public static CornerRadius Pill(double height) => Look.IsBulwark ? Look.ButtonRadius : new CornerRadius(height / 2);

    /// <summary>A list row or a wide card button: the button radius in Bulwark, the given rounder radius elsewhere.</summary>
    public static CornerRadius Row(double other) => Look.IsBulwark ? Look.ButtonRadius : new CornerRadius(other);

    /// <summary>An avatar or a remove button: a circle of this size in every style.</summary>
    public static CornerRadius Circle(double size) => new(size / 2);
}
