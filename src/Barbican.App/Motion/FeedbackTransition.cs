// Mirrors: Bulwark transition-colors (150 ms, 371 uses), buttons transition-all duration-200 (components/ui/button.tsx:42), menu rows duration-150
// (components/ui/context-menu.tsx:663); design/MOTION.md "Feedback". One place decides how a state colour change eases.

namespace Barbican.App.Motion;

/// <summary>
/// Attached property <see cref="KindProperty"/>: put it on the Border (or Panel, ContentPresenter) that draws a control's state
/// colour and the change of its Background eases over the duration of the kind, through the system brush transition (composition: no per-frame
/// work, retargets from where it is, never gates input). The state itself is still applied at once by the VisualStateManager setters. Reduce
/// motion removes the transition (<see cref="Refresh"/>, called when <see cref="MotionRuntime.Scale"/> changes). On the Uno Skia heads the
/// transition is not applied and the colour snaps (feedback is then instant, which design/MOTION.md allows).
/// </summary>
public static class FeedbackTransition
{
    private static readonly List<WeakReference<DependencyObject>> Targets = [];

    /// <summary>The kind of colour transition of an element.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.RegisterAttached(
        "Kind", typeof(FeedbackKind), typeof(FeedbackTransition), new PropertyMetadata(FeedbackKind.None, OnKindChanged));

    public static FeedbackKind GetKind(DependencyObject element) => (FeedbackKind)element.GetValue(KindProperty);

    public static void SetKind(DependencyObject element, FeedbackKind value) => element.SetValue(KindProperty, value);

    /// <summary>The duration of a kind in milliseconds with reduce motion applied.</summary>
    public static double Ms(FeedbackKind kind, MotionScale scale) => kind switch
    {
        FeedbackKind.Color => scale.ColorMs,
        FeedbackKind.Button => scale.ButtonMs,
        FeedbackKind.Menu => scale.MenuMs,
        FeedbackKind.Fast => scale.FastMs,
        _ => 0,
    };

    /// <summary>Applies the current motion scale to every element that has a kind (reduce motion toggled).</summary>
    public static void Refresh()
    {
        for (var i = Targets.Count - 1; i >= 0; i--)
        {
            if (Targets[i].TryGetTarget(out var target))
            {
                Apply(target);
            }
            else
            {
                Targets.RemoveAt(i);
            }
        }
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (GetKind(d) == FeedbackKind.None)
        {
            Apply(d);
            return;
        }

        Targets.Add(new WeakReference<DependencyObject>(d));
        Apply(d);
    }

    private static void Apply(DependencyObject d)
    {
#if WINDOWS
        var ms = Ms(GetKind(d), MotionRuntime.Scale);
        BrushTransition? transition = ms > 0 ? new BrushTransition { Duration = TimeSpan.FromMilliseconds(ms) } : null;
        switch (d)
        {
            case Border border:
                border.BackgroundTransition = transition;
                break;
            case Panel panel:
                panel.BackgroundTransition = transition;
                break;
            case ContentPresenter presenter:
                presenter.BackgroundTransition = transition;
                break;
            default:
                break;
        }
#endif
    }
}