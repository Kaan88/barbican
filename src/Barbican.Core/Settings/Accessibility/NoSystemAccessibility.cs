// Mirrors: Rust a11y.rs System::detect on a non-Windows build (all false). Owner mailcore.

namespace Barbican.Core.Settings.Accessibility;

/// <summary>The default source: the OS reports nothing special (tests, and platforms without a reader yet).</summary>
public sealed class NoSystemAccessibility : ISystemAccessibilitySource
{
    public SystemAccessibility Current => default;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}
