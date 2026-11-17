// Mirrors: Rust motion.rs:30 reduce_for; design/MOTION.md; Bulwark SystemToggle. Stub from F2; owner mailcore.

using Barbican.Core.Common;

namespace Barbican.Core.Settings.Motion;

/// <summary>The reduce-motion rule.</summary>
public static class MotionSettings
{
    /// <summary>What "reduce motion" should be for the stored setting (<c>on</c>, <c>off</c>, <c>system</c>) and the OS animation state.</summary>
    public static bool ReduceFor(string pref, bool systemAnimationsOff) => throw new PortPendingException("MotionSettings.ReduceFor: mailcore stream");
}
