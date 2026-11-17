// Mirrors: design/MOTION.md "Tokens" and rules 2 and 4 (every duration is 0 with reduce motion; list feedback snaps while keys repeat); Rust motion.rs.

using Barbican.Presentation.Theme;

namespace Barbican.App.Motion;

/// <summary>The motion tokens with reduce motion and rapid selection applied. Pure, so the rules are tested without a window.</summary>
public readonly record struct MotionScale(bool Reduce, bool Rapid)
{
    /// <summary>Nothing animates over this (design/MOTION.md rule 2).</summary>
    public const double CeilingMs = 200;

    private double K => Reduce ? 0 : 1;

    /// <summary>Hover, press, focus, selection highlight.</summary>
    public double FastMs => Tokens.Motion.FastMs * K;

    /// <summary>Row feedback of the message list; 0 while the selection moves faster than an animation.</summary>
    public double ListMs => Rapid ? 0 : Tokens.Motion.ListMs * K;

    /// <summary>Menus, popups, flyouts, tooltips, dialogs coming in.</summary>
    public double EnterMs => Tokens.Motion.EnterMs * K;

    /// <summary>The rare thing that animates out.</summary>
    public double ExitMs => Tokens.Motion.ExitMs * K;

    /// <summary>The incoming view after a switch (the content is visible at once).</summary>
    public double ViewMs => Tokens.Motion.ViewMs * K;

    /// <summary>A wait, not motion: kept with reduce motion.</summary>
    public static double TipDelayMs => Tokens.Motion.TipDelayMs;

    /// <summary>The scale an entering popup starts from.</summary>
    public double PopScale => Reduce ? 1 : Tokens.Motion.PopScale;

    /// <summary>How far a toast slides in from.</summary>
    public double RisePx => Reduce ? 0 : Tokens.Motion.RisePx;

    /// <summary>Enter easing, cubic-bezier(0.2, 0, 0, 1) (design/MOTION.md Ease).</summary>
    public static (double X1, double Y1, double X2, double Y2) EnterCurve => Tokens.Ease.Enter;

    /// <summary>Exit easing, cubic-bezier(0.4, 0, 1, 1).</summary>
    public static (double X1, double Y1, double X2, double Y2) ExitCurve => Tokens.Ease.Exit;
}
