// Mirrors: Rust platform/linux.rs open_url (xdg-open); RFC 8252 §4.1. Only http and https URLs and plain tel: numbers are opened (BrowserLaunchRules). Never registered in AUTOMATION
// builds (M1-plan §1: agents never open a real browser).

using System.Diagnostics;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform.Linux;

/// <summary>Opens URLs in the desktop's default browser.</summary>
public sealed class LinuxBrowserLauncher : IBrowserLauncher
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
            var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            start.ArgumentList.Add(uri.AbsoluteUri);
            using var p = Process.Start(start);
            return Task.FromResult(Result<Unit>.Ok(default));
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return Task.FromResult(Result<Unit>.Fail(new TransportError(e.Message, Retryable: false)));
        }
    }
}
