using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Packages.Managed;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Barbican.Core.Tests.Plugins.Packages.Managed;

public sealed class ManagedPluginTransportTests
{
    private static readonly Uri Provider = new("https://policy.example.org/");
    private static readonly Uri Server = new("https://jmap.example.org/");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Code = "exports.activate = () => {};"u8.ToArray();
    private static readonly string TestKey = Convert.ToBase64String(new Ed25519PrivateKeyParameters(new byte[32]).GeneratePublicKey().GetEncoded());

    [Fact]
    public async Task FetchesAuthenticatedBoundedSignedBundleThroughEachFence()
    {
        var key = new Ed25519PrivateKeyParameters(new byte[32]);
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(Code, 0, Code.Length);
        var signature = Convert.ToBase64String(signer.GenerateSignature());
        var handler = new Handler((index, request) => index switch
        {
            1 => Auth(),
            2 => Reply(Registry("privileged")),
            _ => Bundle(signature),
        });
        using var transport = new ManagedPluginTransport(handler);
        var fences = 0;
        var result = await transport.FetchAsync(Provider, Server, "user@example.org", "synthetic-token", "managed-probe",
            Convert.ToBase64String(key.GeneratePublicKey().GetEncoded()), "1.21.2", issue => { fences++; return issue(); }, Ct);
        Assert.True(result.IsOk, result.Error?.Message);
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(fences >= 3);
        Assert.Equal("POST https://policy.example.org/api/auth/stalwart-context", handler.Requests[0]);
        Assert.Equal("GET https://policy.example.org/api/plugins", handler.Requests[1]);
        Assert.Equal("GET https://policy.example.org/api/admin/plugins/managed-probe/bundle", handler.Requests[2]);
        Assert.Equal([null, "jmap_stalwart_ctx=synthetic-cookie", "jmap_stalwart_ctx=synthetic-cookie"], handler.Cookies);
        Assert.All(handler.Authorization, value => Assert.Null(value));
        var signIn = handler.SignIn!.Value;
        Assert.Equal("https://jmap.example.org/", signIn.GetProperty("serverUrl").GetString(), StringComparer.Ordinal);
        Assert.Equal("user@example.org", signIn.GetProperty("username").GetString(), StringComparer.Ordinal);
        Assert.Equal("Bearer synthetic-token", signIn.GetProperty("authHeader").GetString(), StringComparer.Ordinal);
        Assert.Equal(0, signIn.GetProperty("slot").GetInt32());
    }

    [Fact]
    public async Task StaleFenceAndRedirectStopBeforeFurtherRequests()
    {
        var handler = new Handler((_, _) => Auth());
        using var transport = new ManagedPluginTransport(handler);
        var calls = 0;
        var result = await FetchAsync(transport, issue => ++calls == 1 && issue());
        Assert.False(result.IsOk);
        Assert.Single(handler.Requests);
        var redirect = new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.Redirect));
        using var other = new ManagedPluginTransport(redirect);
        Assert.False((await FetchAsync(other, issue => issue())).IsOk);
        Assert.Single(redirect.Requests);
    }

    [Theory]
    [InlineData("jmap_stalwart_ctx=x; Path=/; Secure")]
    [InlineData("jmap_stalwart_ctx=x; Path=/; Secure; HttpOnly; Domain=example.org")]
    [InlineData("jmap_stalwart_ctx=x; Path=/other; Secure; HttpOnly")]
    [InlineData("jmap_stalwart_ctx=x; Path=/; Secure; HttpOnly; Max-Age=0")]
    [InlineData("jmap_stalwart_ctx=x; Path=/; Secure; HttpOnly; Expires=Thu, 01 Jan 1970 00:00:00 GMT")]
    public async Task RejectsUnsafeContextCookies(string cookie)
    {
        var handler = new Handler((_, _) => Auth(cookie));
        using var transport = new ManagedPluginTransport(handler);
        Assert.False((await FetchAsync(transport, issue => issue())).IsOk);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UntrustedRegistryRowNeverFetchesCode()
    {
        var handler = new Handler((index, _) => index == 1 ? Auth() : Reply(Registry("untrusted")));
        using var transport = new ManagedPluginTransport(handler);
        Assert.False((await FetchAsync(transport, issue => issue())).IsOk);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static Task<Barbican.Core.Common.Result<ManagedPluginArtifact>> FetchAsync(ManagedPluginTransport transport, Func<Func<bool>, bool> fence) =>
        transport.FetchAsync(Provider, Server, "user@example.org", "synthetic-token", "managed-probe", TestKey, "1.21.2", fence, Ct);
    private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Auth(string cookie = "jmap_stalwart_ctx=synthetic-cookie; Path=/; Secure; HttpOnly")
    {
        var result = Reply("{\"ok\":true}");
        result.Headers.TryAddWithoutValidation("Set-Cookie", cookie);
        return result;
    }
    private static HttpResponseMessage Bundle(string signature)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Code) };
        response.Headers.TryAddWithoutValidation("X-Bundle-Signature", signature);
        return response;
    }
    private static string Registry(string tier) => new JsonObject { ["plugins"] = new JsonArray(new JsonObject {
        ["id"] = "managed-probe", ["name"] = "Managed probe", ["author"] = "Example", ["version"] = "1.0.0", ["type"] = "hook", ["tier"] = tier, ["dev"] = false,
        ["entrypoint"] = "scripts/plugin.js", ["bundleHash"] = Convert.ToHexStringLower(SHA256.HashData(Code)), ["permissions"] = new JsonArray("crypto:full", "email:raw-send"),
    }) }.ToJsonString();

    private sealed class Handler(Func<int, HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<string?> Cookies { get; } = [];
        public List<string?> Authorization { get; } = [];
        public JsonElement? SignIn { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri);
            Cookies.Add(request.Headers.TryGetValues("Cookie", out var value) ? string.Join("; ", value) : null);
            Authorization.Add(request.Headers.Authorization?.ToString());
            if (request.Content is { } content)
            {
                using var body = JsonDocument.Parse(await content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                SignIn = body.RootElement.Clone();
            }
            return responder(Requests.Count, request);
        }
    }
}
