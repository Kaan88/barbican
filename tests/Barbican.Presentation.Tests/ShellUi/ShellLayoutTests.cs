using Barbican.App.Theme;

namespace Barbican.Presentation.Tests.ShellUi;

/// <summary>spec/layout/shell.md "Breakpoints and responsive rules".</summary>
public sealed class ShellLayoutTests
{
    [Theory]
    [InlineData(1400, false, 368)]
    [InlineData(1100, false, 320)]
    [InlineData(1400, true, 390)]
    [InlineData(1100, true, 330)]
    public void TheListIsNarrowerBelowTheBreakpoint(double width, bool nordic, double list) =>
        Assert.Equal(list, ShellLayout.Compute(width, nordic).ListWidth);

    [Fact]
    public void NordicNeverShowsSummaryTiles() => Assert.False(ShellLayout.Compute(2400, true).SummaryTilesShown);

    [Fact]
    public void BentoShowsSummaryTilesWhenTheReaderIsWideEnough()
    {
        Assert.True(ShellLayout.Compute(1400, false).SummaryTilesShown);
        Assert.False(ShellLayout.Compute(960, false).SummaryTilesShown);
    }

    [Fact]
    public void TheReaderGetsWhatTheColumnsLeave()
    {
        var m = StyleMetrics.Bento;
        var l = ShellLayout.Compute(1400, false);
        Assert.Equal(1400 - (m.Pad + m.RailWidth + m.Gap + m.SidebarWidth + m.Gap + l.ListWidth + m.Gap + m.Pad), l.ReaderWidth, 3);
    }

    [Fact]
    public void ThePlannedMinimumWindowStillGivesTheReaderItsMinimum()
    {
        foreach (var nordic in new[] { true, false })
        {
            Assert.True(ShellLayout.Compute(ShellLayout.MinWidth, nordic).ReaderWidth >= StyleMetrics.For(nordic).ReaderMinWidth);
        }
    }

    [Fact]
    public void BentoAndNordicAreDifferentShapes()
    {
        Assert.NotEqual(StyleMetrics.Bento.RailWidth, StyleMetrics.NordicStyle.RailWidth);
        Assert.True(StyleMetrics.NordicStyle.Hairlines);
        Assert.False(StyleMetrics.Bento.Hairlines);
    }
}
