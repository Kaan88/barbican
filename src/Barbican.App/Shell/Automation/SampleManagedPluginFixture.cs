#if WINDOWS && AUTOMATION
// In-process, synthetic managed provider; never contacts a network service.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Packages.Managed;
using Barbican.Core.Storage;
using Barbican.App.Views.Settings.Automation;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Barbican.App.Shell.Automation;

internal static class SampleManagedPluginFixture
{
    internal const string PluginId = "sample-managed-probe";
    internal const string Origin = "https://managed.example.org";
    internal const string PublicKey = "11qYAYKxCrfVS/7TyWQHOg7hcvPapiMlrwIaaPcHURo=";
    private const string ContextCookie = "synthetic-managed-cookie";
    private const string ManagedActions = """
        let api;
        exports.activate = async host => {
            api = host;
            await api.ui.setActions('composer-header-actions', [
                { id: 'managed-proof', label: 'Check managed add-on', icon: 'edit' },
            ]);
        };
        """;
    private const string ManagedActionHook = """

        exports.hooks.onAction = async event => {
            if (event.actionId === 'managed-proof' && event.context?.composerId)
                await api.toast.success('Managed composer action ran');
        };
        """;

    internal static ManagedPluginSource Create(IAccountSessionSnapshots accounts)
    {
        var profile = Environment.GetEnvironmentVariable("BARBICAN_PROFILE");
        if (!ProfilePaths.IsAutomationProfile(profile) || !Enabled("BARBICAN_SAMPLE") || !Enabled("BARBICAN_SAMPLE_ADDON_ACCOUNT") ||
            !Enabled("BARBICAN_SAMPLE_ORGANIZATION_POLICY") || !Enabled("BARBICAN_SAMPLE_MANAGED_ADDONS"))
            throw new InvalidOperationException("Managed sample fixture is unavailable.");
        var compose = Enabled("BARBICAN_SAMPLE_SEND_PIPELINE") && Enabled("BARBICAN_SAMPLE_MANAGED_COMPOSER");
        return new ManagedPluginSource(accounts, new ManagedPluginTransport(new Handler(compose)));
    }

    private static bool Enabled(string name) => string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private sealed class Handler(bool compose) : HttpMessageHandler
    {
        private readonly byte[] _code = Encoding.UTF8.GetBytes(compose
            ? ManagedActions + AddonsFixtureCommand.TakeoverHook + ManagedActionHook : "exports.activate = () => {};\n");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { } uri || !string.Equals(uri.GetLeftPart(UriPartial.Authority), Origin, StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(uri.Query) || request.Headers.Authorization is not null)
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            if (request.Method == HttpMethod.Post && string.Equals(uri.AbsolutePath, "/api/auth/stalwart-context", StringComparison.Ordinal))
                return await AuthenticateAsync(request, cancellationToken).ConfigureAwait(false);
            if (request.Method != HttpMethod.Get || !request.Headers.TryGetValues("Cookie", out var cookies) ||
                !cookies.SequenceEqual(["jmap_stalwart_ctx=" + ContextCookie], StringComparer.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            if (string.Equals(uri.AbsolutePath, "/api/plugins", StringComparison.Ordinal)) return Json(Registry());
            if (string.Equals(uri.AbsolutePath, "/api/admin/plugins/" + PluginId + "/bundle", StringComparison.Ordinal)) return Bundle();
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static async Task<HttpResponseMessage> AuthenticateAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Headers.Contains("Cookie") || request.Content is null ||
                !request.Headers.TryGetValues("Origin", out var origins) || !origins.SequenceEqual([Origin], StringComparer.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            var body = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !string.Equals(root.GetProperty("serverUrl").GetString(), "https://jmap.example.org/", StringComparison.Ordinal) ||
                !string.Equals(root.GetProperty("username").GetString(), "user@example.org", StringComparison.Ordinal) ||
                !string.Equals(root.GetProperty("authHeader").GetString(), "Bearer " + SamplePolicyFixture.Token, StringComparison.Ordinal) ||
                root.GetProperty("slot").GetInt32() != 0)
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            var response = Json("{\"ok\":true}");
            response.Headers.TryAddWithoutValidation("Set-Cookie", "jmap_stalwart_ctx=" + ContextCookie + "; Path=/; Secure; HttpOnly; SameSite=Lax");
            return response;
        }

        private string Registry() => new JsonObject { ["plugins"] = new JsonArray(new JsonObject
        {
            ["id"] = PluginId, ["name"] = "Synthetic managed probe", ["author"] = "Example", ["version"] = "1.0.0",
            ["type"] = "hook", ["tier"] = "privileged", ["dev"] = false, ["refused"] = false,
            ["entrypoint"] = "scripts/plugin.js", ["bundleHash"] = Convert.ToHexStringLower(SHA256.HashData(_code)),
            ["permissions"] = compose
                ? new JsonArray("crypto:full", "email:raw-send", "email:send", "ui:composer-toolbar")
                : new JsonArray("crypto:full", "email:raw-send"),
        }) }.ToJsonString();

        private HttpResponseMessage Bundle()
        {
            var seed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
            var signer = new Ed25519Signer();
            signer.Init(true, new Ed25519PrivateKeyParameters(seed));
            signer.BlockUpdate(_code, 0, _code.Length);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_code) };
            response.Headers.TryAddWithoutValidation("X-Bundle-Signature", Convert.ToBase64String(signer.GenerateSignature()));
            return response;
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
#endif
