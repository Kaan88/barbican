// Mirrors: design/MOTION.md rule 2 (feedback durations from the tokens, nothing over 200 ms) and rule 4 (reduce motion = 0); Bulwark app/globals.css:1062-1087
// (reduced motion removes every transition). WinUI's stock control templates take their animation lengths from three Duration resources.

namespace Barbican.App.Motion;

/// <summary>
/// The stock controls (toggle, check box, expander, flyouts ...) that are not re-templated yet animate with the WinUI duration resources
/// ControlFasterAnimationDuration (83 ms), ControlFastAnimationDuration (167 ms) and ControlNormalAnimationDuration (250 ms). This maps them onto the shared
/// tokens (FastMs 100, ColorMs 150, ButtonMs 200: nothing over the ceiling) and to 0 with reduce motion, so the setting reaches the Fluent controls too (FM-21).
/// A control picks the value up when its template is created, so a template already on screen keeps the old length until it is rebuilt.
/// </summary>
public static class FluentMotion
{
    /// <summary>Writes the three duration resources for a scale; does nothing before the application exists.</summary>
    public static void Apply(MotionScale scale)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.Resources["ControlFasterAnimationDuration"] = MotionRuntime.Ms(scale.FastMs);
        app.Resources["ControlFastAnimationDuration"] = MotionRuntime.Ms(scale.ColorMs);
        app.Resources["ControlNormalAnimationDuration"] = MotionRuntime.Ms(scale.ButtonMs);
    }
}