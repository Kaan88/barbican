// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:427-477,740-771; native route events revoke before message acceptance.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol.Messages;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Native ingress gate; transport identities are supplied only by the native adapter.</summary>
public sealed class PluginSandboxSession(PluginRuntimeBinding binding, IPluginSandboxWebView view, IPluginApiDispatcher dispatcher, IPluginAuthority authority) : IAsyncDisposable
{
    private static readonly PluginValueLimits WireLimits = new(MaxWireBytes:40 * 1024 * 1024, MaxBinaryBytes:25 * 1024 * 1024, MaxStringBytes:50 * 1024 * 1024);
    private static readonly PluginMessageLimits MessageLimits = new(MaxStringLength:25 * 1024 * 1024, MaxGraphStringLength:25 * 1024 * 1024, MaxGraphBinaryBytes:25 * 1024 * 1024);
    private readonly Lock _gate = new();
    private readonly HashSet<string> _requests = new(StringComparer.Ordinal);
    private bool _ready, _initIssued, _disposed;
    private Task? _cleanup;
    public PluginRuntimeBinding Binding { get; } = binding;
    public IPluginSandboxWebView View { get; } = view;
    public IPluginApiDispatcher Dispatcher { get; } = dispatcher;
    private IPluginAuthority Authority { get; } = authority;
    private bool Active => !_disposed && Binding.IsAlive;
    /// <summary>Direct frame events are rejected without invoking readPayload.</summary>
    public async Task<bool> ReceiveAsync(object sourceView, bool topLevel, Uri source, Func<ReadOnlyMemory<byte>> readPayload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(readPayload);
        lock (_gate) if (!Active || !topLevel || !ReferenceEquals(sourceView, View.RootIdentity) || !Equals(source, View.TrustedRoot)) return false;
        PluginValue value;
        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = readPayload();
            if (bytes.Length > WireLimits.MaxWireBytes) return false;
            value = PluginValueCodec.Decode(bytes, WireLimits);
            if (!PluginMessageValidator.Validate(value,PluginMessageDirection.SandboxToHost,MessageLimits)) return false;
        }
        catch (FormatException) { return false; }
        var type = value.Property("type").StringValue;
        if (string.Equals(type,"sandbox-ready",StringComparison.Ordinal))
        {
            lock (_gate) { if (!Active || _ready) return false; _ready = true; return true; }
        }
        if (!string.Equals(type,"api-request",StringComparison.Ordinal)) return false; // Other protocol handlers land with their ownership registries.
        return await DispatchRequestAsync(value,bytes.Length,ct).ConfigureAwait(false);
    }
    private async Task<bool> DispatchRequestAsync(PluginValue value, int wireLength, CancellationToken ct)
    {
        PluginApiRequest request;
        try { request = new PluginApiRequest(value.Property("id").StringValue,value.Property("method").StringValue,value.Property("args")); }
        catch (ArgumentException) { return false; }
        // storage.get takes a small lookup key, never an attachment or message body.
        if (string.Equals(request.Method,"storage.get",StringComparison.Ordinal) && wireLength > 1024 * 1024) return false;
        lock (_gate)
        {
            if (!Active || !_ready || !_initIssued || _requests.Count >= 128 || !_requests.Add(request.Id)) return false;
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct,Binding.Lifetime);
            if (!await HasAuthorityAsync(linked.Token).ConfigureAwait(false)) return false;
            var outcome = await Dispatcher.DispatchAsync(Binding,request,linked.Token).ConfigureAwait(false);
            if (!await HasAuthorityAsync(linked.Token).ConfigureAwait(false)) return false;
            var encoded = EncodeResponse(request.Id,outcome);
            if (!await HasAuthorityAsync(linked.Token).ConfigureAwait(false)) return false;
            lock (_gate) if (!Active) return false;
            await View.PostAsync(encoded,linked.Token).ConfigureAwait(false);
            return Binding.IsAlive;
        }
        catch (OperationCanceledException) when (!Binding.IsAlive) { return false; }
        finally { lock (_gate) _requests.Remove(request.Id); }
    }
    private async Task<bool> HasAuthorityAsync(CancellationToken ct)
    {
        lock (_gate) if (!Active) return false;
        var current = await Authority.GetCurrentAsync(Binding.Package.Id,ct).ConfigureAwait(false);
        lock (_gate)
        {
            var grant = current.Value;
            return Active && current.IsOk && grant is not null && grant.Enabled && grant.Package == Binding.Package &&
                grant.Generation == Binding.GrantGeneration && grant.Tier == Binding.Tier;
        }
    }
    private static byte[] EncodeResponse(string id, PluginCallOutcome outcome)
    {
        var response = new PluginValueBuilder();
        var result = outcome.IsSuccess ? response.Import(outcome.Value!) : response.String(outcome.ErrorMessage!);
        var envelope = response.Build(response.Object(("type",response.String("api-response")),("id",response.String(id)),
            ("ok",response.Boolean(outcome.IsSuccess)),(outcome.IsSuccess ? "result" : "error",result)),WireLimits);
        return PluginValueCodec.Encode(envelope,WireLimits);
    }
    /// <summary>Issues init after authenticated ready; API calls may arrive during activation.</summary>
    public async Task<bool> IssueInitAsync(PluginValue message, CancellationToken ct)
    {
        if (!PluginMessageValidator.Validate(message,PluginMessageDirection.HostToSandbox,MessageLimits) ||
            !string.Equals(message.Property("type").StringValue,"init",StringComparison.Ordinal)) return false;
        var payload = message.Property("payload");
        if (!string.Equals(payload.Property("pluginId").StringValue,Binding.Package.Id,StringComparison.Ordinal) ||
            !string.Equals(payload.Property("manifest").Property("id").StringValue,Binding.Package.Id,StringComparison.Ordinal) ||
            !string.Equals(payload.Property("manifest").Property("version").StringValue,Binding.Package.Version,StringComparison.Ordinal) ||
            !string.Equals(payload.Property("tier").StringValue,Binding.Tier.ToString(),StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(payload.Property("mode").StringValue,Binding.Kind.ToString(),StringComparison.OrdinalIgnoreCase)) return false;
        var encoded = PluginValueCodec.Encode(message,WireLimits);
        if (!await HasAuthorityAsync(ct).ConfigureAwait(false)) return false;
        lock (_gate) { if (!Active || !_ready || _initIssued) return false; _initIssued = true; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct,Binding.Lifetime);
        try { await View.PostAsync(encoded,linked.Token).ConfigureAwait(false); return Binding.IsAlive; }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
    }
    /// <summary>Invalidates the binding synchronously before asynchronous cleanup starts.</summary>
    public ValueTask NavigationStartingAsync(object sourceView, CancellationToken ct)
    {
        if (!ReferenceEquals(sourceView,View.RootIdentity)) return ValueTask.CompletedTask;
        return DisposeAsync();
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_cleanup is not null) return new(_cleanup);
            _disposed = true;
            var close = Binding.DisposeAsync(); // Binding invalidation occurs synchronously.
            return new(_cleanup = CloseAsync(close));
        }
    }
    private async Task CloseAsync(ValueTask bindingClose)
    {
        try { await bindingClose.ConfigureAwait(false); }
        finally { await View.DisposeAsync().ConfigureAwait(false); }
    }
}
