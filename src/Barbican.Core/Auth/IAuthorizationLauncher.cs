// Mirrors: Rust oauth.rs:159 open_browser (the authorization URL opens in the system browser; RFC 8252 §4.1 "external user-agent").
// Core cannot see Barbican.Platform's IBrowserLauncher (Platform references Core), so the sign-in flow asks this; the platform registration
// adapts the system browser to it, and AUTOMATION builds register RedirectFollowingLauncher instead (never a real browser, M1-plan §1).

using Barbican.Core.Common;

namespace Barbican.Core.Auth;

/// <summary>Shows an authorization URL to the user (a browser) or, in tests and automation, follows it.</summary>
public interface IAuthorizationLauncher
{
    Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct);
}
