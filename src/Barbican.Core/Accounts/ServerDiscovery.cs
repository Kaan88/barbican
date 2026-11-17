// Mirrors: Bulwark lib/auth/mail-server-discovery.ts (from the email domain, try the domain and mail.<domain> for /.well-known/jmap; a JMAP
// server answers it, also with 401 before sign-in) and components/auth/login-server-field.tsx:89 (found: "Server: <host>" with Change);
// RFC 8620 §2.2 (service autodiscovery). Not ported: the _jmap._tcp SRV lookup (.NET has no SRV resolver without a package; FABLE-REVIEW
// "M1 stream: accounts"). spec I-098.

using System.Net;
using Barbican.Core.Jmap.Transport;

namespace Barbican.Core.Accounts;

/// <summary>Finds the JMAP server of an email domain.</summary>
public sealed class ServerDiscovery(IJmapHttp http, TimeProvider time)
{
    /// <summary>How long each probe may take.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The server URL for <paramref name="domain"/>, or null when neither candidate answers like a JMAP server.</summary>
    public async Task<string?> FindAsync(string domain, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.Length == 0)
        {
            return null;
        }

        foreach (var candidate in (string[])[$"https://{domain}", $"https://mail.{domain}"])
        {
            if (await AnswersAsync(new Uri(candidate + "/.well-known/jmap"), ct).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        return null;
    }

    private async Task<bool> AnswersAsync(Uri wellKnown, CancellationToken ct)
    {
        using var deadline = new CancellationTokenSource(ProbeTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, wellKnown);
            using var response = await http.ClientFor(wellKnown).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);

            // a session (200), a redirect to it, or a request to sign in first: all say "a JMAP server lives here"
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized || (int)response.StatusCode is >= 300 and < 400;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
