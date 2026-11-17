// Mirrors: Rust a11y.rs:46 System (what Windows reports for the settings left on "System"). Stub from F2; owner mailcore.

namespace Barbican.Core.Settings.Accessibility;

/// <summary>What the OS reports for the accessibility settings left on "System".</summary>
public readonly record struct SystemAccessibility(bool HighContrast, bool ReduceMotion, bool Touch);
