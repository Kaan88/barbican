// Mirrors: Rust oauth.rs:159 open_browser (platform::open_url). Adapts the platform's browser launcher to the sign-in flow in Core, which
// cannot reference this project.

using Barbican.Core.Auth;
using Barbican.Core.Common;

namespace Barbican.Platform;

/// <summary>Opens the authorization URL in the system browser.</summary>
public sealed class BrowserAuthorizationLauncher(IBrowserLauncher browser) : IAuthorizationLauncher
{
    /// <inheritdoc/>
    public Task<Result<Unit>> OpenAsync(Uri authorizeUri, CancellationToken ct) => browser.OpenAsync(authorizeUri, ct);
}
