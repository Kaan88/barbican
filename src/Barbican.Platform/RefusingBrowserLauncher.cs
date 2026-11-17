// Mirrors: M1-plan §1 rule 2 ("Never open a real browser"), ADR 0007. The browser launcher of AUTOMATION builds: it records the URL and opens
// nothing (the OAuth sign-in there goes through RedirectFollowingLauncher instead).

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Platform;

/// <summary>A browser launcher that never opens anything.</summary>
public sealed class RefusingBrowserLauncher : IBrowserLauncher
{
    private readonly List<Uri> _refused = [];

    /// <summary>The URLs that were not opened.</summary>
    public IReadOnlyList<Uri> Refused
    {
        get
        {
            lock (_refused)
            {
                return [.. _refused];
            }
        }
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> OpenAsync(Uri uri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (_refused)
        {
            _refused.Add(uri);
        }

        return Task.FromResult(Result<Unit>.Fail(new TransportError("This build does not open a browser.", Retryable: false)));
    }
}
