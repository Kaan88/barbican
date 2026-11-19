using Barbican.App.Shell;

namespace Barbican.Presentation.Tests.ShellUi;

public sealed class ReplyDockLayoutTests
{
    [Theory]
    [InlineData(700, 420)]
    [InlineData(846, 508)]
    [InlineData(1200, 720)]
    public void TallPaneStartsAtSixtyPercent(double pane, double height)
    {
        var layout = ReplyDockLayout.Compute(pane);
        Assert.False(layout.Takeover);
        Assert.Equal(height, layout.Height);
    }

    [Theory]
    [InlineData(699)]
    [InlineData(586)]
    [InlineData(300)]
    public void ShortPaneUsesTakeover(double pane)
    {
        var layout = ReplyDockLayout.Compute(pane);
        Assert.True(layout.Takeover);
        Assert.Equal(pane, layout.Height);
    }

    [Theory]
    [InlineData(0.75, 675)]
    [InlineData(0.95, 700)]
    [InlineData(0.2, 320)]
    [InlineData(double.NaN, 540)]
    public void SavedShareFitsTheAvailablePane(double share, double expected)
    {
        Assert.Equal(expected, ReplyDockLayout.Compute(900, share).Height);
        Assert.True(ReplyDockLayout.Compute(650, share).Takeover);
    }

    [Fact]
    public void UnmeasuredPaneWaitsForLayout()
    {
        Assert.Equal(new ReplyDockLayout(false, 0), ReplyDockLayout.Compute(0));
        Assert.Equal(new ReplyDockLayout(false, 0), ReplyDockLayout.Compute(double.NaN));
    }
}
