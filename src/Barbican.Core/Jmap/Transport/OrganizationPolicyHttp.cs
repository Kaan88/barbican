// Dedicated policy HTTP transport: no redirects, cookies, proxy or ambient credentials.
using System.Net;

namespace Barbican.Core.Jmap.Transport;

internal sealed class OrganizationPolicyHttp : IDisposable
{
    private readonly HttpClient _client;
    internal OrganizationPolicyHttp() : this(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        Credentials = null, AutomaticDecompression = DecompressionMethods.None,
    }) { }
    internal OrganizationPolicyHttp(HttpMessageHandler handler)
    {
#pragma warning disable RS0030 // Dedicated bounded policy transport; the handler never follows redirects or sends ambient credentials.
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
#pragma warning restore RS0030
    }
    internal Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    public void Dispose() => _client.Dispose();
}
