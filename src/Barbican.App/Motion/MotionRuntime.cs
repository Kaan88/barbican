// Mirrors: design/MOTION.md rules 1, 2 and 4 and "Building blocks" (PopIn, Scrim, Tip); Rust motion.rs (reduce, rapid).

using Microsoft.UI.Xaml.Media.Animation;

namespace Barbican.App.Motion;

/// <summary>
/// The motion tokens as the shell currently has them (reduce motion and rapid selection applied) and the one way views animate:
/// an enter animation that starts from where the element is and never delays anything (design/MOTION.md rule 1).
/// </summary>
public static class MotionRuntime
{
    private static MotionScale _scale = new(Reduce: false, Rapid: false);

    /// <summary>Updated by the theme controller when the shell's reduce motion or rapid state changes. Reduce motion also reaches the colour transitions
    /// of the feedback styles and the animation durations of the stock controls (<see cref="FluentMotion"/>).</summary>
    public static MotionScale Scale
    {
        get => _scale;
        set
        {
            var reduceChanged = _scale.Reduce != value.Reduce;
            _scale = value;
            if (reduceChanged)
            {
                FeedbackTransition.Refresh();
                FluentMotion.Apply(value);
            }
        }
    }

    /// <summary>True while the Bulwark style is on (set with the shape dictionary, <see cref="Theme.AppResources.UseShape(Barbican.Presentation.Theme.ThemeVariant)"/>).</summary>
    public static bool Bulwark { get; set; }

    /// <summary>
    /// A duration for motion that Bulwark does not have: the list refresh rise, the reader switch rise and body fade, a menu entering, a tooltip fading,
    /// the shell rise on a style switch. Zero in the Bulwark style (the change is on the first frame), unchanged in Bento and Nordic.
    /// </summary>
    public static double DecorationMs(double ms) => Bulwark ? 0 : ms;

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
