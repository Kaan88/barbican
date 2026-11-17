// Several fake servers behind one transport, picked by origin (two accounts on two servers).

namespace Barbican.Core.Tests.Accounts;

internal sealed class OriginRouter(params (string Origin, HttpMessageHandler Handler)[] routes) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var origin = request.RequestUri!.GetLeftPart(UriPartial.Authority);
        foreach (var (o, h) in routes)
        {
            if (string.Equals(o, origin, StringComparison.OrdinalIgnoreCase))
            {
                using var invoker = new HttpMessageInvoker(h, disposeHandler: false);
                return await invoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
