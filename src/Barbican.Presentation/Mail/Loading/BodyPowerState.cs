// Mirrors: decision D-WV-01 (BodyBackgroundPolicy).

namespace Barbican.Presentation.Mail.Loading;

/// <summary>What the body web view is doing for the window's visibility.</summary>
public enum BodyPowerState
{
    /// <summary>Running (visible, or awake in the background because a message was opened there).</summary>
    Active,

    /// <summary>Hidden, on the low memory target and suspended.</summary>
    Suspended,

    /// <summary>Closed; the next use creates it again.</summary>
    Closed,
}
