#if WINDOWS && AUTOMATION
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Barbican.Core.Auth;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.OrganizationPolicy;
using Barbican.Core.Storage;
using Barbican.FakeJmap;

namespace Barbican.App.Shell.Automation;

/// <summary>In-process OAuth and policy endpoints used only by explicit synthetic automation runs.</summary>
internal sealed class SamplePolicyFixture : IDisposable
{
    internal const string Token = "synthetic-policy-access-123";
    private readonly JmapHttp _http;
    private readonly OAuthTokenSource _tokens;
    internal SamplePolicyFixture(FakeJmapServer server, JmapClient initial, IProfilePaths paths, TimeProvider time)
    {
        _http = new JmapHttp(() => new FakeJmapBearerHandler(server));
        _tokens = new OAuthTokenSource(new OAuthHttp(_http, time), new EmptyRefreshStore(), paths, time, "sample",
            new OAuthTokens(Token, null, null, new Uri(server.Base + "/oauth/token"), "sample"));
        Client = new JmapClient(_http, _tokens, initial.Session, new JmapClientOptions(), time, JmapTrace.Off);
    }
    internal JmapClient Client { get; }
    internal static OrganizationPolicyTransport Transport() => new(new PolicyHandler());
    internal static bool Enabled(IProfilePaths paths) => ProfilePaths.IsAutomationProfile(paths.Profile) &&
        Enabled("BARBICAN_SAMPLE") && Enabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") && Enabled("BARBICAN_SAMPLE_ORGANIZATION_POLICY");
    internal static bool PrintEnabled(IProfilePaths paths) => Enabled(paths) && Enabled("BARBICAN_SAMPLE_PRINT") &&
        (Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PRINT_POLICY") is "allow" or "watermark" or "deny");
    private static bool Enabled(string name) => string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);
    public void Dispose() { Client.Dispose(); _tokens.Dispose(); _http.Dispose(); }

    private sealed class EmptyRefreshStore : IRefreshTokenStore
    {
        public Task<string?> ReadAsync(string accountKey, CancellationToken ct) => Task.FromResult<string?>(null);
        public Task WriteAsync(string accountKey, string refresh, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeJmapBearerHandler(FakeJmapServer server) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker _fake = new(server, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!string.Equals(request.RequestUri?.GetLeftPart(UriPartial.Authority), server.Options.Origin, StringComparison.Ordinal) ||
                !string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal) ||
                !string.Equals(request.Headers.Authorization?.Parameter, Token, StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", FakeJmapServer.AccessToken);
            return _fake.SendAsync(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { if (disposing) _fake.Dispose(); base.Dispose(disposing); }
    }

    private sealed class PolicyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || !string.Equals(request.RequestUri?.AbsoluteUri, "https://policy.example.org/api/admin/policy", StringComparison.Ordinal) ||
                !string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal) ||
                !string.Equals(request.Headers.Authorization?.Parameter, Token, StringComparison.Ordinal) ||
                !request.Headers.TryGetValues("X-JMAP-Server-Url", out var values) || !values.SequenceEqual(["https://jmap.example.org/"], StringComparer.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            var profile = Environment.GetEnvironmentVariable("BARBICAN_PROFILE") ?? string.Empty;
            var printing = ProfilePaths.IsAutomationProfile(profile) && Enabled("BARBICAN_SAMPLE") && Enabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") &&
                Enabled("BARBICAN_SAMPLE_ORGANIZATION_POLICY") && Enabled("BARBICAN_SAMPLE_PRINT") &&
                (Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PRINT_POLICY") is "allow" or "watermark");
            var watermark = printing && string.Equals(Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PRINT_POLICY"), "watermark", StringComparison.Ordinal);
            var labels = SampleLabels(profile);
            var json = "{\"organisation\":{\"devices\":{\"noPrint\":" + (printing ? "false" : "true") + ",\"watermark\":" + (watermark ? "true" : "false") + "}" + labels + PhishingReport(profile) + "},\"scopes\":[]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.TryAddWithoutValidation("X-Bulwark-Policy-Domains", "example.org");
            return Task.FromResult(response);
        }

        // G-050: the admin's report address (a reserved example domain: nothing reaches a real mailbox) and format, in a synthetic phishing-report run only
        private static string PhishingReport(string profile)
        {
            var mode = Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_PHISHING_REPORT");
            if (!ProfilePaths.IsAutomationProfile(profile) || !Enabled("BARBICAN_SAMPLE") || !Enabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") ||
                !Enabled("BARBICAN_SAMPLE_ORGANIZATION_POLICY") || mode is not ("1" or "arf"))
                return string.Empty;
            return ",\"phishingReport\":{\"address\":\"security@example.org\",\"format\":\"" + (mode is "arf" ? "arf" : "forward") + "\"}";
        }

        private static string SampleLabels(string profile)
        {
            var mode = Environment.GetEnvironmentVariable("BARBICAN_SAMPLE_RAW_LABELS");
            if (!ProfilePaths.IsAutomationProfile(profile) || !Enabled("BARBICAN_SAMPLE") || !Enabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") ||
                !Enabled("BARBICAN_SAMPLE_ORGANIZATION_POLICY") || !Enabled("BARBICAN_SAMPLE_SEND_PIPELINE") || mode is not ("1" or "required" or "takeover"))
                return string.Empty;
            if (mode is "takeover")
                return ",\"labels\":{\"list\":[{\"id\":\"protected\",\"name\":\"Synthetic protected\",\"color\":\"#d97706\",\"subjectPrefix\":\"[Protected]\"},{\"id\":\"internal\",\"name\":\"Synthetic internal\",\"color\":\"#2563eb\"}],\"required\":true}";
            var required = mode is "required" ? "true" : "false";
            return ",\"labels\":{\"list\":[{\"id\":\"synthetic\",\"name\":\"Synthetic label\",\"color\":\"#d97706\"}],\"defaultId\":\"synthetic\",\"required\":" + required + "}";
        }
    }
}
#endif
