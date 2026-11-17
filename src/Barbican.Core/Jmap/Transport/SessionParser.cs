// Mirrors: RFC 8620 §2 (the Session object; URLs "MAY be relative"); Rust jmap.rs:65 absolute, :139 open (apiUrl and a primary
// mail account required, gates from the core limits); Bulwark lib/jmap/client.ts:2183 connect (accounts object, apiUrl and downloadUrl
// required), :7391 getEventSourceUrl (session root, then the core capability); Stalwart crates/jmap/src/api/session.rs. Owner jmap.

using System.Text.Json;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Reads a session document into a <see cref="JmapSession"/>.</summary>
public static class SessionParser
{
    /// <summary>The session, or a <see cref="TransportError"/> naming what is missing or unsafe.</summary>
    /// <param name="json">The session document.</param>
    /// <param name="sessionUri">The URL it was read from (after redirects): relative URLs resolve against it (RFC 8620 §2).</param>
    /// <param name="server">The server the user typed: every URL is moved onto its origin unless it is a deliberate https host of its own.</param>
    /// <param name="viaOtherOrigin">The document came from another origin than <paramref name="server"/>'s (fetched without credentials): no
    /// URL of it is kept off the typed origin.</param>
    public static Result<JmapSession> Parse(string json, Uri sessionUri, ServerEndpoint server, bool viaOtherOrigin)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(sessionUri);
        ArgumentNullException.ThrowIfNull(server);
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Fail("The server's session is not valid JSON.");
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("accounts", out var accounts) || accounts.ValueKind != JsonValueKind.Object
            || Str(root, "apiUrl") is not { } api || Str(root, "downloadUrl") is not { } download)
        {
            return Fail("No mail account found in session");
        }

        var primary = Primary(root);
        if (!primary.ContainsKey(JmapCapabilities.Mail))
        {
            return Fail("Account has no mail capability");
        }

        var caps = root.TryGetProperty("capabilities", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var core = caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty(JmapCapabilities.Core, out var k) ? k : default;
        var eventSource = Str(root, "eventSourceUrl") ?? Str(core, "eventSourceUrl") ?? "";
        var urls = new SessionUrls(sessionUri, server, AbsoluteOrigin(api), viaOtherOrigin);
        var apiUrl = urls.Move(api);
        var downloadUrl = urls.Rewrite(download);
        var uploadUrl = Str(root, "uploadUrl") is { } up ? urls.Rewrite(up) : "";
        var eventSourceUrl = eventSource.Length == 0 ? "" : urls.Rewrite(eventSource);
        if (string.Equals(sessionUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && ((string[])[apiUrl, downloadUrl, uploadUrl, eventSourceUrl]).Any(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
        {
            return Fail("The server's session names an insecure (http) address.");
        }

        return Result<JmapSession>.Ok(new JmapSession
        {
            ApiUrl = apiUrl,
            DownloadUrl = downloadUrl,
            UploadUrl = uploadUrl,
            EventSourceUrl = eventSourceUrl,
            State = Str(root, "state") ?? "",
            Username = Str(root, "username") ?? "",
            SessionUrl = sessionUri.ToString(),
            ServerUrl = server.BaseUri.ToString(),
            CredentialOrigins = urls.Origins,
            PrimaryAccounts = primary,
            CapabilityNames = caps.ValueKind == JsonValueKind.Object ? [.. caps.EnumerateObject().Select(x => x.Name)] : [],
            Limits = Limits(core),
            Raw = root,
        });
    }

    /// <summary>
    /// A session URL made absolute against the URL the session came from. Done on the string, not through <see cref="Uri"/>, so the
    /// <c>{accountId}</c> template braces stay unescaped.
    /// </summary>
    public static string Absolute(Uri sessionUri, string url)
    {
        ArgumentNullException.ThrowIfNull(sessionUri);
        ArgumentNullException.ThrowIfNull(url);
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return sessionUri.Scheme + ":" + url;
        }

        var origin = sessionUri.GetLeftPart(UriPartial.Authority);
        if (url.StartsWith('/'))
        {
            return origin + url;
        }

        var path = sessionUri.AbsolutePath;
        return origin + path[..(path.LastIndexOf('/') + 1)] + url;
    }

    /// <summary>The origin (scheme, host, port) of an absolute http(s) URL as written, or null (relative, protocol-relative or unparsable).</summary>
    public static string? AbsoluteOrigin(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? u.GetLeftPart(UriPartial.Authority)
            : null;
    }

    private static Dictionary<string, string> Primary(JsonElement root)
    {
        var primary = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("primaryAccounts", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            foreach (var kv in p.EnumerateObject())
            {
                if (kv.Value.ValueKind == JsonValueKind.String)
                {
                    primary[kv.Name] = kv.Value.GetString()!;
                }
            }
        }

        return primary;
    }

    private static CoreLimits Limits(JsonElement core)
    {
        var d = new CoreLimits();
        if (core.ValueKind != JsonValueKind.Object)
        {
            return d;
        }

        return new CoreLimits
        {
            MaxSizeUpload = Num(core, "maxSizeUpload") ?? d.MaxSizeUpload,
            MaxConcurrentUpload = (int)Math.Clamp(Num(core, "maxConcurrentUpload") ?? d.MaxConcurrentUpload, 1, 64),
            MaxSizeRequest = Num(core, "maxSizeRequest") ?? d.MaxSizeRequest,
            MaxConcurrentRequests = (int)Math.Clamp(Num(core, "maxConcurrentRequests") ?? d.MaxConcurrentRequests, 1, 64),
            MaxCallsInRequest = (int)Math.Clamp(Num(core, "maxCallsInRequest") ?? d.MaxCallsInRequest, 1, 4096),
            MaxObjectsInGet = (int)Math.Clamp(Num(core, "maxObjectsInGet") ?? d.MaxObjectsInGet, 1, 1_000_000),
            MaxObjectsInSet = (int)Math.Clamp(Num(core, "maxObjectsInSet") ?? d.MaxObjectsInSet, 1, 1_000_000),
        };
    }

    private static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Num(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : null;

    private static Result<JmapSession> Fail(string message) => Result<JmapSession>.Fail(new TransportError(message, Retryable: false));
}
