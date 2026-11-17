// Mirrors: Rust mailview.rs:278 parse_key result (modifiers, key text). Stub from F2; owner body.

namespace Barbican.Core.Mail.Body;

/// <summary>A key the mail view caught and the shell replays in the native window.</summary>
/// <param name="Modifiers">The held modifiers.</param>
/// <param name="Text">The key text: one character, or U+001B (Escape), U+007F (Delete), the F5 and F9 key characters.</param>
public sealed record ForwardedKey(KeyModifiers Modifiers, string Text);
