// Mirrors: design/MOTION.md rules 1, 2 and 4 and "Building blocks" (PopIn, Scrim, Tip); Rust motion.rs (reduce, rapid).

using Microsoft.UI.Xaml.Media.Animation;

namespace Barbican.App.Motion;

/// <summary>
/// The motion tokens as the shell currently has them (reduce motion and rapid selection applied) and the one way views animate:
/// an enter animation that starts from where the element is and never delays anything (design/MOTION.md rule 1).
/// </summary>
public static class MotionRuntime
{
    /// <summary>Updated by the theme controller when the shell's reduce motion or rapid state changes.</summary>
    public static MotionScale Scale { get; set; } = new(Reduce: false, Rapid: false);

    /// <summary>A duration in milliseconds as a Duration.</summary>
    public static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(ms));

    /// <summary>Fades an element in over Motion.Enter with the enter curve; with reduce motion it is shown at once.</summary>
    public static void FadeIn(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var ms = Scale.EnterMs;
        if (ms <= 0)
        {
            element.Opacity = 1;
            return;
        }

        element.Opacity = 0;
        var curve = MotionScale.EnterCurve;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = Ms(ms) };
        frames.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
            Value = 1,
            KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(curve.X1, curve.Y1), ControlPoint2 = new Windows.Foundation.Point(curve.X2, curve.Y2) },
        });
        Storyboard.SetTarget(frames, element);
        Storyboard.SetTargetProperty(frames, "Opacity");
        var board = new Storyboard();
        board.Children.Add(frames);
        board.Completed += (_, _) => element.Opacity = 1;
        board.Begin();
    }
}
