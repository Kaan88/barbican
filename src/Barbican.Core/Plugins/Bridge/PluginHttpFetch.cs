// Mirrors Bulwark lib/plugin-sandbox/host-api.ts http.fetch. The Windows host authorizes every redirect before sending it.
using System.Text;
using Barbican.Core.Net;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Bridge;

public sealed class PluginHttpFetch : IPluginApiMethod
{
    private static readonly PluginValueLimits ResultLimits = new(MaxWireBytes: 40 * 1024 * 1024,
        MaxBinaryBytes: GuardedHttp.PluginBodyLimit, MaxStringBytes: 50 * 1024 * 1024);
    private readonly IPluginDeliveryAuthority _authority;
    private readonly Func<PluginFetchRequest, CancellationToken, Task<PluginFetchReply?>> _send;
    private readonly Storage.IPrefsStore _prefs;
    private int _transfers;

    public PluginHttpFetch(IPluginDeliveryAuthority authority, GuardedHttp transport, Storage.IPrefsStore prefs)
        : this(authority, transport.SendPluginHopAsync, prefs) { }

    internal PluginHttpFetch(IPluginDeliveryAuthority authority,
        Func<PluginFetchRequest, CancellationToken, Task<PluginFetchReply?>> send, Storage.IPrefsStore prefs)
    {
        _authority = authority;
        _send = send;
        _prefs = prefs;
    }

    public PluginApiDescriptor Descriptor { get; } = new("http.fetch", [[ "http:fetch" ]], TimeSpan.FromSeconds(120), mayMutate: true);

#if AUTOMATION
    /// <summary>Fixed response-only transport for the private WebView test.</summary>
    public static PluginHttpFetch ForSyntheticProbe(IPluginDeliveryAuthority authority, Storage.IPrefsStore prefs) =>
        new(authority, static (request, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(request.Uri.IdnHost, "probe.example.org", StringComparison.Ordinal) || request.Method != HttpMethod.Get)
                return Task.FromResult<PluginFetchReply?>(null);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x-probe"] = "synthetic" };
            if (string.Equals(request.Uri.AbsolutePath, "/redirect", StringComparison.Ordinal))
                return Task.FromResult<PluginFetchReply?>(new(302, "Found", headers, [], new Uri("https://elsewhere.example.org/result")));
            if (!string.Equals(request.Uri.AbsolutePath, "/max", StringComparison.Ordinal))
                return Task.FromResult<PluginFetchReply?>(null);
            var body = new byte[GuardedHttp.PluginBodyLimit];
            body.AsSpan().Fill((byte)'x');
            return Task.FromResult<PluginFetchReply?>(new(200, "OK", headers, body));
        }, prefs);
#endif

    public PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        Read(arguments) is null ? PluginErrorCode.InvalidArguments : null;

    public async Task<PluginCallOutcome> InvokeAsync(IPluginInvocationContext context, PluginValue arguments, CancellationToken ct)
    {
        if (Read(arguments) is not { } parsed) return Failed(PluginErrorCode.InvalidArguments);
        if (PluginNetworkPolicy.IsBlocked(_prefs)) return Failed(PluginErrorCode.PermissionDenied);
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _transfers) > 2)
        {
            Interlocked.Decrement(ref _transfers);
            return Failed(PluginErrorCode.RateLimited);
        }
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            void PolicyChanged(object? sender, EventArgs args)
            {
                if (PluginNetworkPolicy.IsBlocked(_prefs)) _ = CancelAsync(stop);
            }
            _prefs.Changed += PolicyChanged;
            try { return await FetchAsync(context, parsed, stop.Token).ConfigureAwait(false); }
            finally { _prefs.Changed -= PolicyChanged; }
        }
        finally { Interlocked.Decrement(ref _transfers); }
    }

    private async Task<PluginCallOutcome> FetchAsync(IPluginInvocationContext context, Input parsed, CancellationToken ct)
    {
        var hop = new PluginFetchRequest(parsed.Uri, parsed.Method, parsed.Headers, ReadBody(parsed.Body));
        var binding = context.Binding;
        bool CanConnect() => binding.IsAlive && !ct.IsCancellationRequested && !PluginNetworkPolicy.IsBlocked(_prefs);
        for (var redirects = 0; redirects <= 20; redirects++)
        {
            if (!CanConnect() || !await context.CheckCurrentAsync(ct).ConfigureAwait(false))
                return Interrupted(context, PluginErrorCode.StaleContext);
            Task<PluginFetchReply?>? outbound = null;
            var current = hop with { CanConnect = CanConnect };
            var admitted = await _authority.TryPostAsync(binding, grant =>
            {
                if (!CanConnect() || grant.Tier != binding.Tier ||
                    !grant.Authorizes(binding.Package, binding.GrantGeneration, "http:fetch", null) ||
                    !grant.ApprovedHttpOrigins.SetEquals(context.Grant.ApprovedHttpOrigins) ||
                    !MatchesOrigin(current.Uri, grant.ApprovedHttpOrigins)) return false;
                return binding.TryWriteWhileAlive(() =>
                {
                    if (!CanConnect()) return;
                    if (current.Method != HttpMethod.Get && current.Method != HttpMethod.Head && current.Method != HttpMethod.Options)
                        context.MarkMutationIssued();
                    outbound = _send(current, ct);
                });
            }, ct).ConfigureAwait(false);
            if (!admitted || outbound is null) return Interrupted(context, PluginErrorCode.PermissionDenied);
#pragma warning disable VSTHRD003 // The host transport starts after authorization and completes without the UI or authority lock.
            var reply = await outbound.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            if (reply is null || !CanConnect()) return Interrupted(context, PluginErrorCode.Failed);
            if (reply.Location is not { } location || reply.Status is not (301 or 302 or 303 or 307 or 308))
                return Project(reply);
            if (redirects == 20 || !Uri.TryCreate(hop.Uri, location, out var next) ||
                !MatchesOrigin(next, context.Grant.ApprovedHttpOrigins))
                return Interrupted(context, PluginErrorCode.PermissionDenied);

            var rewrite = ((reply.Status is 301 or 302) && hop.Method == HttpMethod.Post) ||
                (reply.Status == 303 && hop.Method != HttpMethod.Get && hop.Method != HttpMethod.Head);
            var sameOrigin = string.Equals(next.IdnHost, hop.Uri.IdnHost, StringComparison.OrdinalIgnoreCase) && next.Port == hop.Uri.Port;
            if (!sameOrigin && !rewrite && hop.Body is not null) return Interrupted(context, PluginErrorCode.PermissionDenied);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sameOrigin)
            {
                foreach (var (name, value) in hop.Headers)
                    if (!rewrite || !name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)) headers.Add(name, value);
            }
            hop = new(next, rewrite ? HttpMethod.Get : hop.Method, headers, rewrite ? null : hop.Body);
        }
        return Interrupted(context, PluginErrorCode.Failed);
    }

    private static async Task CancelAsync(CancellationTokenSource stop)
    {
        try { await stop.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
    }

    private static PluginCallOutcome Interrupted(IPluginInvocationContext context, PluginErrorCode code) =>
        Failed(context.HasIssuedMutation ? PluginErrorCode.UnknownOutcome : code);
    private static PluginCallOutcome Failed(PluginErrorCode code) => PluginCallOutcome.Failed(code);

    private PluginCallOutcome Project(PluginFetchReply reply)
    {
        if (reply.Body.Length > GuardedHttp.PluginBodyLimit) return Failed(PluginErrorCode.Failed);
        var builder = new PluginValueBuilder();
        var headers = builder.Object([.. reply.Headers.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key.ToLowerInvariant(), builder.String(pair.Value)))]);
        var bytes = builder.View(builder.Buffer(reply.Body), PluginViewType.Uint8, 0, reply.Body.Length);
        return PluginCallOutcome.FetchSucceeded(builder.Build(builder.Object(("ok", builder.Boolean(reply.Status is >= 200 and < 300)),
            ("status", builder.Number(reply.Status)), ("statusText", builder.String(reply.StatusText)),
            ("headers", headers), ("bodyText", builder.Null()), ("bodyBytes", bytes)), ResultLimits),
            () => !PluginNetworkPolicy.IsBlocked(_prefs));
    }

    private static bool MatchesOrigin(Uri uri, IEnumerable<string> allowed)
    {
        if (!IsHttps(uri)) return false;
        foreach (var entry in allowed)
        {
            var wildcard = entry.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase);
            if (!Uri.TryCreate(wildcard ? entry.Remove(8, 2) : entry, UriKind.Absolute, out var origin) ||
                !IsHttps(origin) || !string.Equals(origin.AbsolutePath, "/", StringComparison.Ordinal) ||
                origin.Query.Length != 0 || origin.Port != uri.Port) continue;
            var host = uri.IdnHost;
            var expected = origin.IdnHost;
            if (!wildcard && string.Equals(host, expected, StringComparison.OrdinalIgnoreCase)) return true;
            if (!wildcard || !host.EndsWith("." + expected, StringComparison.OrdinalIgnoreCase)) continue;
            var prefix = host.AsSpan(0, host.Length - expected.Length - 1);
            if (!prefix.IsEmpty && !prefix.Contains('.')) return true;
        }
        return false;
    }

    private static bool IsHttps(Uri uri) => uri.IsAbsoluteUri && !uri.IsLoopback && uri.HostNameType == UriHostNameType.Dns &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && uri.UserInfo.Length == 0 &&
        uri.Fragment.Length == 0 && uri.Port > 0;

    private static Input? Read(PluginValue args)
    {
        if (args is not { Kind: PluginValueKind.Array, Count: 1 or 2 } ||
            args.Item(0) is not { Kind: PluginValueKind.String, StringValue.Length: > 0 and <= 8192 } url ||
            !Uri.TryCreate(url.StringValue, UriKind.Absolute, out var uri) || !IsHttps(uri)) return null;
        var options = args.Count == 2 ? args.Item(1) : null;
        var method = HttpMethod.Get;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PluginValue? body = null;
        if (options is { Kind: not (PluginValueKind.Undefined or PluginValueKind.Null) })
        {
            if (options.Kind != PluginValueKind.Object) return null;
            foreach (var key in options.PropertyNames)
            {
                var value = options.Property(key);
                if (value.Kind == PluginValueKind.Undefined && key is "method" or "headers" or "body") continue;
                switch (key)
                {
                    case "method":
                        if (value is not { Kind: PluginValueKind.String } || ReadMethod(value.StringValue) is not { } parsed) return null;
                        method = parsed;
                        break;
                    case "headers":
                        if (!ReadHeaders(value, headers)) return null;
                        break;
                    case "body":
                        if (value.Kind is PluginValueKind.Null) break;
                        if (!BodyFits(value)) return null;
                        body = value;
                        break;
                    default: return null;
                }
            }
        }
        if (body is not null && (method == HttpMethod.Get || method == HttpMethod.Head)) return null;
        if (body is { Kind: PluginValueKind.String }) headers.TryAdd("Content-Type", "text/plain;charset=UTF-8");
        return new(uri, method, headers, body);
    }

    private static HttpMethod? ReadMethod(string value)
    {
        if (!Token(value, 64)) return null;
        var upper = value.ToUpperInvariant();
        if (upper is "CONNECT" or "TRACE" or "TRACK") return null;
        return new HttpMethod(upper is "DELETE" or "GET" or "HEAD" or "OPTIONS" or "POST" or "PUT" ? upper : value);
    }

    private static bool ReadHeaders(PluginValue input, Dictionary<string, string> headers)
    {
        if (input is not { Kind: PluginValueKind.Object, Count: <= 64 }) return false;
        foreach (var name in input.PropertyNames)
        {
            var value = input.Property(name);
            if (!Token(name, 128) || value is not { Kind: PluginValueKind.String, StringValue.Length: <= 8192 } ||
                value.StringValue.Any(c => c is < ' ' or > '~')) return false;
            var key = name.ToLowerInvariant();
            if (key is "cookie" or "cookie2" or "x-jmap-username" or "referer" or "origin" or "host" or "content-length" or
                "connection" or "transfer-encoding" or "upgrade" or "te" or "trailer" or "keep-alive" or "expect" or "accept-encoding" or
                "forwarded" or "via" || key.StartsWith("proxy-", StringComparison.Ordinal) || key.StartsWith("sec-", StringComparison.Ordinal) ||
                key.StartsWith("x-forwarded-", StringComparison.Ordinal)) continue;
            if (!headers.TryAdd(key, value.StringValue)) return false;
        }
        return true;
    }

    private static bool Token(string text, int limit) => text.Length <= limit && text.Length > 0 &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c));

    private static bool BodyFits(PluginValue value) => value.Kind switch
    {
        PluginValueKind.String => Encoding.UTF8.GetByteCount(value.StringValue) <= GuardedHttp.PluginBodyLimit,
        PluginValueKind.ArrayBuffer or PluginValueKind.View => value.ByteLength <= GuardedHttp.PluginBodyLimit,
        _ => false,
    };

    private static byte[]? ReadBody(PluginValue? body) => body is null ? null :
        body.Kind == PluginValueKind.String ? Encoding.UTF8.GetBytes(body.StringValue) : body.BinaryBytes();

    private sealed record Input(Uri Uri, HttpMethod Method, Dictionary<string, string> Headers, PluginValue? Body);
}
