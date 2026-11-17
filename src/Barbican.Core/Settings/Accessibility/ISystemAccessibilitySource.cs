// Mirrors: Rust a11y.rs System::detect :71 (SPI_GETHIGHCONTRAST, SPI_GETCLIENTAREAANIMATION, SM_MAXIMUMTOUCHES); Linux reads the portal. Owner mailcore
// (interface); the platform streams implement it per OS.

namespace Barbican.Core.Settings.Accessibility;

/// <summary>What the OS reports for the accessibility settings left on "System".</summary>
public interface ISystemAccessibilitySource
{
    /// <summary>The current OS state.</summary>
    SystemAccessibility Current { get; }

    /// <summary>The OS state may have changed (the window was activated): read <see cref="Current"/> again.</summary>
    event EventHandler? Changed;
}
