using Barbican.App.Motion;

namespace Barbican.Presentation.Tests.ShellUi;

/// <summary>design/MOTION.md rules 2 and 4: nothing animates over 200 ms; reduce motion makes every duration 0 (a wait is not motion).</summary>
public sealed class MotionScaleTests
{
    [Fact]
    public void NothingAnimatesOverTheCeiling()
    {
        var m = new MotionScale(false, false);
        Assert.All(new[] { m.FastMs, m.ListMs, m.EnterMs, m.ExitMs, m.ViewMs, m.ColorMs, m.ButtonMs, m.MenuMs }, d => Assert.InRange(d, 0, MotionScale.CeilingMs));
    }

    [Fact]
    public void ReduceMotionZeroesEveryDuration()
    {
        var m = new MotionScale(true, false);
        Assert.Equal(new[] { 0.0, 0, 0, 0, 0, 0, 0, 0 }, new[] { m.FastMs, m.ListMs, m.EnterMs, m.ExitMs, m.ViewMs, m.ColorMs, m.ButtonMs, m.MenuMs });
        Assert.Equal(1, m.PopScale);
        Assert.Equal(0, m.RisePx);
    }

    [Fact]
    public void FeedbackDurationsAreBulwarksTransitionLengths()
    {
        var m = new MotionScale(false, false);
        Assert.Equal(new[] { 150.0, 200, 150 }, new[] { m.ColorMs, m.ButtonMs, m.MenuMs });
    }

    [Fact]
    public void TheTipDelayIsAWaitAndSurvivesReduceMotion() => Assert.Equal(350, MotionScale.TipDelayMs);

    [Fact]
    public void RapidSelectionSnapsTheListFeedbackOnly()
    {
        var m = new MotionScale(false, true);
        Assert.Equal(0, m.ListMs);
        Assert.True(m.FastMs > 0);
    }
}
