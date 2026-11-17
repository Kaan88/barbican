// Ported from Rust src/motion.rs test (:81).

using Barbican.Core.Settings.Motion;

namespace Barbican.Core.Tests.Settings.Motion;

public sealed class MotionSettingsTests
{
    [Fact]
    [RustTest("motion::setting_wins_over_system")]
    public void Setting_wins_over_system()
    {
        Assert.True(MotionSettings.ReduceFor("on", false));
        Assert.False(MotionSettings.ReduceFor("off", true));
        Assert.True(MotionSettings.ReduceFor("system", true));
        Assert.False(MotionSettings.ReduceFor("system", false));
        Assert.False(MotionSettings.ReduceFor("", false));
    }
}
