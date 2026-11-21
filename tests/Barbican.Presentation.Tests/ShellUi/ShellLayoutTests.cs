using Barbican.App.Theme;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Tests.ShellUi;

/// <summary>spec/layout/shell.md "Breakpoints and responsive rules".</summary>
public sealed class ShellLayoutTests
{
    [Theory]
    [InlineData(1400, false, 370)]
    [InlineData(1100, false, 320)]
    [InlineData(1400, true, 390)]
    [InlineData(1100, true, 330)]
    public void LegacyListWidthsArePreserved(double width, bool nordic, double list) =>
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
        var l = ShellLayout.Compute(1100, false);
        Assert.Equal(1100 - (m.PadLeft + m.RailWidth + m.Gap + m.SidebarWidth + m.Gap + l.ListWidth + m.Gap + m.Pad), l.ReaderWidth, 3);
    }

    [Fact]
    public void ThePlannedMinimumWindowStillGivesTheReaderItsMinimum()
    {
        foreach (var nordic in new[] { true, false })
        {
            Assert.True(ShellLayout.Compute(ShellLayout.MinWidth, nordic).ReaderWidth >= StyleMetrics.For(nordic).ReaderMinWidth);
        }
    }

    // Bulwark hooks/use-media-query.ts:12-33 (PHONE_BELOW = 600, classifyWidth: isMobile is width < 600), edge-drawer.tsx:63-66 (w-72, max-w-[85%])
    [Theory]
    [InlineData(599.5, true)]
    [InlineData(600, false)]
    [InlineData(844, false)]
    [InlineData(360, true)]
    public void ThePhoneLayoutBeginsBelowSixHundredPixels(double width, bool phone) => Assert.Equal(phone, ShellLayout.IsPhone(width));

    [Theory]
    [InlineData(1280, 288)]
    [InlineData(599, 288)]
    [InlineData(320, 272)]
    public void TheFolderDrawerIsAtMostEightyFivePercentOfAWindow(double width, double drawer) => Assert.Equal(drawer, ShellLayout.DrawerWidthFor(width), 3);

    [Fact]
    public void TheBulwarkWindowIsNeverBlockedFromThePhoneWidth() =>
        Assert.True(ShellLayout.PhoneBelow < ShellLayout.MinWidth, "the 960 px floor is the legacy styles' planning minimum; Bulwark's phone layout starts below 600 and no window minimum is enforced");

    [Theory]
    [InlineData(1560, false, true)]
    [InlineData(1400, false, false)]
    [InlineData(1560, true, true)]
    [InlineData(1100, true, false)]
    public void TheSenderPanelShowsOnlyWhenTheReaderKeepsItsRoom(double width, bool nordic, bool shown)
    {
        var l = ShellLayout.Compute(width, nordic);
        Assert.Equal(shown, l.ContextShown);
        if (shown)
        {
            Assert.True(l.ReaderWidth >= StyleMetrics.For(nordic).ContextMinReader);
        }
    }

    [Fact]
    public void TheSenderPanelTakesItsWidthFromTheReader()
    {
        var m = StyleMetrics.Bento;
        var l = ShellLayout.Compute(1560, false);
        Assert.Equal(1560 - (m.PadLeft + m.RailWidth + m.Gap + m.SidebarWidth + m.Gap + l.ListWidth + m.Gap + m.Pad) - m.ContextWidth - m.Gap, l.ReaderWidth, 3);
    }

    [Fact]
    public void DefaultColumnEdgesMatchReference()
    {
        var m = StyleMetrics.For(ShellStyle.Bulwark);
        Assert.Equal(56, m.RailWidth);
        Assert.Equal(312, m.PadLeft + m.RailWidth + m.Gap + m.SidebarWidth + m.Gap);
        Assert.Equal(696, m.PadLeft + m.RailWidth + m.Gap + m.SidebarWidth + m.Gap + m.ListWidth(2013));
        Assert.Equal(384, ShellLayout.Compute(960, ShellStyle.Bulwark).ListWidth);
        Assert.True(ShellLayout.Compute(960, ShellStyle.Bulwark).ReaderWidth >= m.ReaderMinWidth);
    }

    [Fact]
    public void BulwarkOmitsLegacySummaryAndContext()
    {
        var layout = ShellLayout.Compute(2013, ShellStyle.Bulwark);
        Assert.False(layout.SummaryTilesShown);
        Assert.False(layout.ContextShown);
        Assert.Equal(2013 - 696, layout.ReaderWidth);
    }

    [Fact]
    public void LegacyShellMetricsRemainUnchanged()
    {
        Assert.Equal((14d, 14d, 12d, 66d, 236d), (StyleMetrics.Bento.PadLeft, StyleMetrics.Bento.Pad, StyleMetrics.Bento.Gap, StyleMetrics.Bento.RailWidth, StyleMetrics.Bento.SidebarWidth));
        Assert.Equal((0d, 14d, 0d, 76d, 228d), (StyleMetrics.NordicStyle.PadLeft, StyleMetrics.NordicStyle.Pad, StyleMetrics.NordicStyle.Gap, StyleMetrics.NordicStyle.RailWidth, StyleMetrics.NordicStyle.SidebarWidth));
    }
}
