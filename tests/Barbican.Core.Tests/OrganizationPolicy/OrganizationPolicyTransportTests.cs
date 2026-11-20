using System.Net;
using System.Text;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class OrganizationPolicyTransportTests
{
    private static readonly Uri Origin = new("https://policy.example.org/");
    private static readonly Uri Server = new("https://jmap.example.org/");
    private const string Token = "synthetic-token-123456";

    [Fact]
    public async Task Authenticated_policy_is_bounded_and_bound_to_the_captured_server()
    {
        var handler = new Handler(() => Reply(HttpStatusCode.OK, "{\"features\":{}}", "example.org,EXAMPLE.ORG"));
        using var transport = new OrganizationPolicyTransport(handler);
        var result = await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Authoritative, result.Status);
        Assert.Equal(["example.org"], result.Domains, StringComparer.Ordinal);
        Assert.Equal("https://policy.example.org/api/admin/policy", handler.Target);
        Assert.Equal(Server.AbsoluteUri, handler.Server);
        Assert.Equal("Bearer", handler.Scheme);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, OrganizationPolicyTransportStatus.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, OrganizationPolicyTransportStatus.Missing)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OrganizationPolicyTransportStatus.Unavailable)]
    [InlineData(HttpStatusCode.Redirect, OrganizationPolicyTransportStatus.Unavailable)]
    public async Task Failures_are_not_authoritative(HttpStatusCode code, OrganizationPolicyTransportStatus expected)
    {
        using var transport = new OrganizationPolicyTransport(new Handler(() => Reply(code, "{}", null)));
        var result = await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Domains);
        Assert.Null(result.Policy);
    }

    [Fact]
    public async Task Public_missing_proof_malformed_and_refused_handoff_are_distinct()
    {
        var replies = new Queue<HttpResponseMessage>();
        var publicReply = Reply(HttpStatusCode.OK, "{}", null);
        publicReply.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Scope", "public");
        replies.Enqueue(publicReply);
        replies.Enqueue(Reply(HttpStatusCode.OK, "{}", null));
        replies.Enqueue(Reply(HttpStatusCode.OK, "{", "example.org"));
        var handler = new Handler(replies.Dequeue);
        using var transport = new OrganizationPolicyTransport(handler);
        Assert.Equal(OrganizationPolicyTransportStatus.Public, (await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None)).Status);
        Assert.Equal(OrganizationPolicyTransportStatus.Untrusted, (await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None)).Status);
        Assert.Equal(OrganizationPolicyTransportStatus.Malformed, (await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None)).Status);
        Assert.Equal(OrganizationPolicyTransportStatus.Stale, (await transport.ReadAsync(Origin, Server, Token, _ => false, CancellationToken.None)).Status);
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("public, private")]
    [InlineData("public, public")]
    public async Task Scope_header_with_ambiguous_or_unknown_value_is_untrusted(string scope)
    {
        HttpResponseMessage Response()
        {
            var response = Reply(HttpStatusCode.OK, "{}", "example.org");
            response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Scope", scope);
            return response;
        }
        using var transport = new OrganizationPolicyTransport(new Handler(Response));
        var result = await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Untrusted, result.Status);
        Assert.Null(result.Policy);
    }

    [Fact]
    public async Task Multiple_scope_headers_are_untrusted()
    {
        HttpResponseMessage Response()
        {
            var response = Reply(HttpStatusCode.OK, "{}", "example.org");
            response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Scope", "public");
            response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Scope", "public");
            return response;
        }
        using var transport = new OrganizationPolicyTransport(new Handler(Response));
        var result = await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Untrusted, result.Status);
    }

    [Theory]
    [InlineData("bücher.example", OrganizationPolicyTransportStatus.Authoritative)]
    [InlineData("bücher.example,xn--bcher-kva.example", OrganizationPolicyTransportStatus.Malformed)]
    [InlineData("example.org,,other.example", OrganizationPolicyTransportStatus.Malformed)]
    [InlineData("example.org.", OrganizationPolicyTransportStatus.Malformed)]
    public async Task Coverage_domains_are_canonical_and_ambiguous_values_are_rejected(string domains, OrganizationPolicyTransportStatus expected)
    {
        using var transport = new OrganizationPolicyTransport(new Handler(() => Reply(HttpStatusCode.OK, "{}", domains)));
        var result = await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None);
        Assert.Equal(expected, result.Status);
        if (expected == OrganizationPolicyTransportStatus.Authoritative) Assert.Equal(["xn--bcher-kva.example"], result.Domains, StringComparer.Ordinal);
        else Assert.Empty(result.Domains);
    }

    [Fact]
    public async Task Body_timeout_is_bounded_and_caller_cancellation_propagates()
    {
        static HttpResponseMessage Blocked()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) };
            response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Domains", "example.org");
            return response;
        }
        using var transport = new OrganizationPolicyTransport(new Handler(Blocked), TimeSpan.FromMilliseconds(50));
        var result = await transport.ReadAsync(Origin, Server, Token, issue => issue(), CancellationToken.None);
        Assert.Equal(OrganizationPolicyTransportStatus.Unavailable, result.Status);
        using var cancel = new CancellationTokenSource();
        cancel.CancelAfter(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.ReadAsync(Origin, Server, Token, issue => issue(), cancel.Token));
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Reply(HttpStatusCode code, string json, string? domains)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (domains is not null) response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Domains", domains);
        return response;
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public string? Target { get; private set; }
        public string? Server { get; private set; }
        public string? Scheme { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            Target = request.RequestUri?.AbsoluteUri;
            Server = request.Headers.GetValues("X-JMAP-Server-Url").Single();
            Scheme = request.Headers.Authorization?.Scheme;
            return Task.FromResult(response());
        }
    }
}
