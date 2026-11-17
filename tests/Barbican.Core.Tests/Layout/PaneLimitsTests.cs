// Ported from Rust src/dnd.rs test pane_limits (:381). Red until stream actions implements PaneLimits (M2 plan 6.1, T-090).

using Barbican.Core.Layout;

namespace Barbican.Core.Tests.Layout;

public sealed class PaneLimitsTests
{
    [Fact]
    [RustTest("dnd::pane_limits")]
    public void Pane_limits()
    {
        Assert.Equal((PaneLimits.SideMin, PaneLimits.ListMax), PaneLimits.Clamp(100f, 900f));
        Assert.Equal((250f, 300f), PaneLimits.Clamp(250f, 300f));
        Assert.Equal((PaneLimits.SideDefault, PaneLimits.ListDefault), PaneLimits.Clamp(float.NaN, 0f));

        // nothing saved: the style decides the width
        Assert.Equal((0f, 0f), PaneLimits.Clamp(0f, -5f));
    }
}
