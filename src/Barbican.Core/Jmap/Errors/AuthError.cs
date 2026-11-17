// Mirrors: Rust oauth.rs (NEEDS_SIGN_IN), Bulwark lib/jmap/client.ts request 401 handling.

namespace Barbican.Core.Jmap.Errors;

/// <summary>The server rejected the credentials.</summary>
/// <param name="Message">What to show.</param>
/// <param name="NeedsSignIn">True when nothing newer is stored and the user must sign in again.</param>
public sealed record AuthError(string Message, bool NeedsSignIn) : JmapError(Message);
