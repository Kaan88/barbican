// Mirrors: Rust mailview.rs:289 (the c, s, a flags of a forwarded key). Stub from F2; owner body.

namespace Barbican.Core.Mail.Body;

/// <summary>Modifiers of a key the mail view forwards.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
}
