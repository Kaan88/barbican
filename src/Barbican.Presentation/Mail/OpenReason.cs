// Mirrors: Rust msgload.rs (open by click, key, prefetch); M1-plan §4.3. Contract stub from F2; owner mailcore.

namespace Barbican.Presentation.Mail;

/// <summary>Why a message is being opened.</summary>
public enum OpenReason
{
    Click,
    Key,
    Search,
    Push,
}
