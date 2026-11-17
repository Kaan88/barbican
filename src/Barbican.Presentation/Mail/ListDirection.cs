// Mirrors: Rust msgload.rs prefetch in the direction of travel; M1-plan §4.3. Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>Which way the selection is moving (prefetch goes that way).</summary>
public enum ListDirection
{
    None,
    Down,
    Up,
}
