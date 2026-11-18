// Mirrors: Rust platform/windows.rs open_url (ShellExecute of an http(s) URL in the default browser); RFC 8252 §4.1 (an external user-agent
// for the authorization request). Only http and https URLs and plain tel: numbers are opened (BrowserLaunchRules). Never registered in AUTOMATION builds (M1-plan §1: agents never open a
// real browser).

using System.Diagnostics;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform.Windows;

/// <summary>Opens URLs in the user's default browser.</summary>
public sealed class WindowsBrowserLauncher : IBrowserLauncher
{
    /// <inheritdoc/>
    public Task<Result<Unit>> OpenAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!BrowserLaunchRules.Allows(uri))
        {
            return Task.FromResult(Result<Unit>.Fail(new TransportError("Only web addresses and phone numbers open outside the app.", Retryable: false)));
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return Task.FromResult(Result<Unit>.Ok(default));
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return Task.FromResult(Result<Unit>.Fail(new TransportError(e.Message, Retryable: false)));
        }
    }
}
