// Mirrors: Bulwark lib/plugin-sandbox/host-bridge.ts:427-477,740-771; native route events revoke before message acceptance.
using System.Text;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Protocol.Messages;
using Barbican.Core.Plugins.Runtime;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Native ingress gate; transport identities are supplied only by the native adapter.</summary>
public sealed partial class PluginSandboxSession(PluginRuntimeBinding binding, IPluginSandboxWebView view, IPluginApiDispatcher dispatcher, IPluginAuthority authority) : IAsyncDisposable
{
    private static readonly PluginValueLimits WireLimits = new(MaxWireBytes:40 * 1024 * 1024, MaxBinaryBytes:25 * 1024 * 1024, MaxStringBytes:50 * 1024 * 1024);
    private static readonly PluginMessageLimits MessageLimits = new(MaxStringLength:25 * 1024 * 1024, MaxGraphStringLength:25 * 1024 * 1024, MaxGraphBinaryBytes:25 * 1024 * 1024);
    private readonly Lock _gate = new();
    private readonly HashSet<string> _requests = new(StringComparer.Ordinal);
    private readonly PluginSandboxHookReplies _hookReplies = new(binding);
    private readonly TaskCompletionSource _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<PluginValue?> _initializationSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _ready, _initQueued, _initIssued, _disposed;
    private bool _activationFailed;
    private Task? _cleanup;
    public PluginRuntimeBinding Binding { get; } = binding;
    public IPluginSandboxWebView View { get; } = view;
    public IPluginApiDispatcher Dispatcher { get; } = dispatcher;
    private IPluginAuthority Authority { get; } = authority;
    private bool Active => !_disposed && Binding.IsAlive;
#if AUTOMATION
    internal bool InitIssued { get { lock (_gate) return _initIssued; } }
#endif

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
            // This control is emitted by the trusted parent, never relayed from a plugin message.
            if (bytes.Span.SequenceEqual("barbican-sandbox-transport-closed"u8))
            {
                await DisposeAsync().ConfigureAwait(false);
                return true;
            }
            value = PluginValueCodec.Decode(bytes, WireLimits);
            if (!PluginMessageValidator.Validate(value,PluginMessageDirection.SandboxToHost,MessageLimits)) return false;
        }
        catch (FormatException) { return false; }
        var type = value.Property("type").StringValue;
        if (string.Equals(type,"sandbox-ready",StringComparison.Ordinal))
        {
            lock (_gate) { if (!Active || _ready) return false; _ready = true; _readySignal.TrySetResult(); return true; }
        }
        if (string.Equals(type,"init-done",StringComparison.Ordinal) || string.Equals(type,"init-error",StringComparison.Ordinal))
        {
            return await CompleteInitializationAsync(string.Equals(type,"init-done",StringComparison.Ordinal) ? value : null).ConfigureAwait(false);
        }
        if (string.Equals(type, "hook-result", StringComparison.Ordinal))
        {
            var id = value.Property("id").StringValue;
            lock (_gate) return Active && _hookReplies.TryComplete(Binding, id, value);
        }
        if (!string.Equals(type,"api-request",StringComparison.Ordinal)) return false;
        return await DispatchRequestAsync(value,bytes.Length,ct).ConfigureAwait(false);
    }
    /// <summary>Waits for authenticated readiness or cancellation of the caller or binding.</summary>
    public async Task WaitForReadyAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Binding.Lifetime);
        await _readySignal.Task.WaitAsync(linked.Token).ConfigureAwait(false);
    }
    /// <summary>Returns the authenticated initialization result, or null for an initialization error.</summary>
    public async Task<PluginValue?> WaitForInitializationAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Binding.Lifetime);
        try
        {
            return await _initializationSignal.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && Volatile.Read(ref _activationFailed))
        {
            // Closing the failed binding can wake the waiter before its result continuation runs.
            return null;
        }
    }

    private async Task<bool> CompleteInitializationAsync(PluginValue? value)
    {
        ValueTask close;
        lock (_gate)
        {
            if (!Active || !_initIssued || _initializationSignal.Task.IsCompleted) return false;
            _initializationSignal.TrySetResult(value);
            if (value is not null) return true;
            Volatile.Write(ref _activationFailed, true);
            close = DisposeAsync();
        }

        await close.ConfigureAwait(false);
        return true;
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
            return await View.PostAsync((write, token) => DeliverResponseAsync(request.Id, outcome, write, token), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!Binding.IsAlive) { return false; }
        finally { lock (_gate) _requests.Remove(request.Id); }
    }
    private async ValueTask<bool> DeliverResponseAsync(string id, PluginCallOutcome outcome, Action<string> write, CancellationToken ct)
    {
        if (Authority is not IPluginDeliveryAuthority delivery) return false;
        var encoded = Encoding.UTF8.GetString(EncodeResponse(id, outcome));
        var refused = outcome.IsSuccess ? Encoding.UTF8.GetString(EncodeResponse(id, outcome.RefuseDelivery())) : encoded;
        return await delivery.TryPostAsync(Binding, grant =>
        {
            lock (_gate)
            {
                if (!Active || ct.IsCancellationRequested) return false;

                // The grant, all account captures and binding stay current through this synchronous post.
                return outcome.TryWriteForDelivery(Binding, grant, () => write(encoded))
                    || Binding.TryWriteWhileAlive(() => write(refused));
            }
        }, ct).ConfigureAwait(true);
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
        var fields = new List<(string, int)>
        {
            ("type", response.String("api-response")), ("id", response.String(id)),
            ("ok", response.Boolean(outcome.IsSuccess)), (outcome.IsSuccess ? "result" : "error", result),
        };
        if (outcome.ReconstructFetchBodyText) fields.Add(("decodeFetchBody", response.Boolean(true)));
        var envelope = response.Build(response.Object([.. fields]), WireLimits);
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
        var encoded = Encoding.UTF8.GetString(PluginValueCodec.Encode(message, WireLimits));
        if (!await HasAuthorityAsync(ct).ConfigureAwait(false)) return false;
        lock (_gate) { if (!Active || !_ready || _initQueued || _initIssued) return false; _initQueued = true; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct,Binding.Lifetime);
        try
        {
            if (Authority is not IPluginDeliveryAuthority delivery) return false;
            return await View.PostAsync((write, token) => new(delivery.TryPostAsync(Binding, _ =>
            {
                lock (_gate)
                {
                    if (!Active || token.IsCancellationRequested || !Binding.TryWriteWhileAlive(() => write(encoded))) return false;
                    _initIssued = true;
                    return true;
                }
            }, token)), linked.Token).ConfigureAwait(false);
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
        finally { lock (_gate) _initQueued = false; }
    }
    /// <summary>Issues managed init only while the exact startup proof remains current at the native write.</summary>
    public async Task<bool> IssueManagedInitAsync(PluginRuntimeStart request,
        Barbican.Core.Plugins.Packages.Managed.ManagedPluginStartupProof proof, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(proof);
        if (Binding.Tier != Barbican.Core.Plugins.Permissions.PluginExecutionTier.Privileged || proof.Package != Binding.Package ||
            request.Grant.Package != Binding.Package || request.Grant.Generation != Binding.GrantGeneration ||
            request.Grant.Tier != Binding.Tier || request.Kind != Binding.Kind || request.Installation.Package.Identity != Binding.Package ||
            !string.Equals(request.Profile, Binding.Profile, StringComparison.Ordinal) || !string.Equals(request.Slot, Binding.Slot, StringComparison.Ordinal) ||
            !await HasAuthorityAsync(ct).ConfigureAwait(false)) return false;
        lock (_gate) { if (!Active || !_ready || _initQueued || _initIssued) return false; _initQueued = true; }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Binding.Lifetime);
        try
        {
            if (Authority is not IPluginDeliveryAuthority delivery) return false;
            return await View.PostAsync((write, token) => new(delivery.TryPostAsync(Binding, _ =>
            {
                lock (_gate)
                {
                    if (!Active || token.IsCancellationRequested) return false;
                    return proof.TryReadEntrypoint(code =>
                    {
                        var message = PluginSandboxInitMessage.Create(request, code);
                        var encoded = Encoding.UTF8.GetString(PluginValueCodec.Encode(message, WireLimits));
                        if (token.IsCancellationRequested || !Binding.TryWriteWhileAlive(() => write(encoded))) return false;
                        _initIssued = true;
                        return true;
                    });
                }
            }, token)), linked.Token).ConfigureAwait(false);
        }
        catch { await DisposeAsync().ConfigureAwait(false); throw; }
        finally { lock (_gate) _initQueued = false; }
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
            _hookReplies.Dispose();
            return new(_cleanup = CloseAsync(close));
        }
    }
    private async Task CloseAsync(ValueTask bindingClose)
    {
        try { await bindingClose.ConfigureAwait(false); }
        finally { await View.DisposeAsync().ConfigureAwait(false); }
    }
}
