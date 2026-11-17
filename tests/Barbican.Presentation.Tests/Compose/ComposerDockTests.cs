using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Tests.Compose;

/// <summary>Rust src/compose.rs test reply_dock_choice, ported (M3 stream composer-ui).</summary>
public sealed class ComposerDockTests
{
    [Fact]
    [Red("composer-ui")]
    [RustTest("compose::reply_dock_choice")]
    public void Reply_dock_choice()
    {
        Assert.True(ComposerDock.ShouldDock(true, false, true, false));
        Assert.False(ComposerDock.ShouldDock(false, false, true, false));
        Assert.False(ComposerDock.ShouldDock(true, true, true, false));
        Assert.False(ComposerDock.ShouldDock(true, false, false, false));
        Assert.False(ComposerDock.ShouldDock(true, false, true, true));
    }
}
