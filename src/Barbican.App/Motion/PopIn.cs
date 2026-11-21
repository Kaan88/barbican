// Mirrors: design/MOTION.md "Building blocks": PopIn fades in from 0 and scales from Motion.pop-scale over Motion.enter, removed instantly,
// transform-origin set to the anchor corner (the account switcher, menus, dialog cards).

using Microsoft.UI.Xaml.Media.Animation;

namespace Barbican.App.Motion;

/// <summary>The enter behaviour of a popup or flyout card: opacity 0 to 1 and scale pop-scale to 1 over Motion.enter. Nothing waits for it.</summary>
public static class PopIn
{
    /// <summary>Plays the animation on <paramref name="card"/>, growing from <paramref name="origin"/> (0..1 of its size). A <see cref="PopInKind.Menu"/> shows at once in the Bulwark style (it has no entrance animation).</summary>
    public static void Run(FrameworkElement card, Windows.Foundation.Point origin, PopInKind kind = PopInKind.Dialog)
    {
        ArgumentNullException.ThrowIfNull(card);
        var scale = MotionRuntime.Scale;
        var ms = kind == PopInKind.Menu ? MotionRuntime.DecorationMs(scale.EnterMs) : scale.EnterMs;
        if (ms <= 0)
        {
            card.Opacity = 1;
            card.RenderTransform = null;
            return;
        }

        var transform = new ScaleTransform { ScaleX = scale.PopScale, ScaleY = scale.PopScale };
        card.RenderTransformOrigin = origin;
        card.RenderTransform = transform;
        card.Opacity = 0;
        var curve = MotionScale.EnterCurve;
        var board = new Storyboard();
        foreach (var (target, property, to) in new (DependencyObject, string, double)[] { (card, "Opacity", 1), (transform, "ScaleX", 1), (transform, "ScaleY", 1) })
        {
            var frames = new DoubleAnimationUsingKeyFrames { Duration = MotionRuntime.Ms(ms) };
            frames.KeyFrames.Add(new SplineDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
                Value = to,
                KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(curve.X1, curve.Y1), ControlPoint2 = new Windows.Foundation.Point(curve.X2, curve.Y2) },
            });
            Storyboard.SetTarget(frames, target);
            Storyboard.SetTargetProperty(frames, property);
            board.Children.Add(frames);
        }

        board.Completed += (_, _) =>
        {
            card.Opacity = 1;
            card.RenderTransform = null;
        };
        board.Begin();
    }
}
