// Mirrors: Rust motion.rs:30 reduce_for; design/MOTION.md; Bulwark lib/reduced-motion.ts (SystemToggle). Owner mailcore.

namespace Barbican.Core.Settings.Motion;

/// <summary>The reduce-motion rule and the timings of "rapid" list movement.</summary>
public static class MotionSettings
{
    /// <summary>Steps closer together than this are a key repeat (or a very fast hand): list feedback snaps (Rust motion.rs RAPID).</summary>
    public static TimeSpan RapidWithin { get; } = TimeSpan.FromMilliseconds(150);

    /// <summary>How long after the last fast step the list goes back to animating (Rust motion.rs CALM_AFTER).</summary>
    public static TimeSpan CalmAfter { get; } = TimeSpan.FromMilliseconds(180);

    /// <summary>What "reduce motion" should be for the stored setting (<c>on</c>, <c>off</c>, <c>system</c>) and the OS animation state.</summary>
    public static bool ReduceFor(string pref, bool systemAnimationsOff)
    {
        ArgumentNullException.ThrowIfNull(pref);
        return pref switch { "on" => true, "off" => false, _ => systemAnimationsOff };
    }
}
