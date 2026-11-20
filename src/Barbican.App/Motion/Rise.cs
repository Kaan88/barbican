// Mirrors: design/MOTION.md rule 2 "view / app switch: the new content is visible on the first frame; at most a short fade/slide of the incoming view";
// the WinUI prototype the user approved (prototype/winui/MainWindow.xaml.cs ApplyStyle: the shell rises 10 px from opacity 0.55 on a style switch;
// the reader header's ContentThemeTransition, a 6 px rise; the body's 110 ms fade-in), shortened to the motion tokens (docs/milestones/M1-look-gap.md).

using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media.Animation;

namespace Barbican.App.Motion;

/// <summary>
/// The incoming-view decoration: an element that already shows its new content rises a few pixels and goes from a partly transparent start to full
/// opacity, with the enter curve. Content is there on the first frame (it never starts invisible), nothing waits for it, and a new run replaces a
/// running one at once (rule 1: never queue). Duration 0 (reduce motion, rapid key repeat) does nothing.
/// </summary>
public static class Rise
{
    private static readonly ConditionalWeakTable<UIElement, Storyboard> Running = new();

    /// <summary>Plays the rise on <paramref name="element"/>, moving <paramref name="shift"/> (the element's own TranslateTransform).</summary>
    public static void Run(UIElement element, TranslateTransform shift, double ms, double fromOpacity = 0.6, double fromY = 8)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(shift);
        if (Running.TryGetValue(element, out var old))
        {
            old.Stop();
            Running.Remove(element);
        }

        if (ms <= 0)
        {
            return;
        }

        var board = new Storyboard { FillBehavior = FillBehavior.Stop };
        board.Children.Add(Frames(element, "Opacity", fromOpacity, 1, ms));
        board.Children.Add(Frames(shift, "Y", fromY, 0, ms));
        board.Completed += (_, _) =>
        {
            if (Running.TryGetValue(element, out var current) && ReferenceEquals(current, board))
            {
                Running.Remove(element);
            }
        };
        Running.AddOrUpdate(element, board);
        board.Begin();
    }

    private static DoubleAnimationUsingKeyFrames Frames(DependencyObject target, string property, double from, double to, double ms)
    {
        var curve = MotionScale.EnterCurve;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = MotionRuntime.Ms(ms) };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        frames.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
            Value = to,
            KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(curve.X1, curve.Y1), ControlPoint2 = new Windows.Foundation.Point(curve.X2, curve.Y2) },
        });
        Storyboard.SetTarget(frames, target);
        Storyboard.SetTargetProperty(frames, property);
        return frames;
    }
}
