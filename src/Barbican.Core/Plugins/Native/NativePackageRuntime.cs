using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Plugins.Packages.Inspection;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Core.Plugins.Native;

internal sealed class NativePackageRuntime(NativePlugins plugins, NativePluginHost host,
    PluginRuntimeBinding binding) : IPluginRuntimeInstance
{
    private readonly Lock _gate = new();
    private IDisposable? _registration;
    private Task<bool>? _startup;
    private Task? _closing;
    private int _published;

    public PluginRuntimeBinding Binding => binding;
    public PluginActivation Activation { get; } = new([], [], []);
    internal NativePluginHost Host => host;
    internal bool IsPublished => Volatile.Read(ref _published) != 0;

    internal void Publish()
    {
        host.PublishApprovedArchive();
        Volatile.Write(ref _published, 1);
    }

    internal void Attach(IDisposable registration)
    {
        lock (_gate)
        {
            if (_closing is null) _registration = registration;
            else registration.Dispose();
        }
    }

    internal void QueueStartup(InspectedPluginPackage archive)
    {
        lock (_gate)
        {
            if (_closing is not null) return;
            _startup = Task.Run(async () =>
            {
                try
                {
                    await host.InitializeApprovedArchiveAsync(archive, binding, binding.Lifetime).ConfigureAwait(false);
                    return binding.IsAlive;
                }
                catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
                {
                    if (binding.IsAlive) host.Report(archive.Id, "initialize", error);
                    return false;
                }
            });
        }
    }

    internal Task<bool> WaitForStartupAsync(CancellationToken ct)
    {
        lock (_gate) return _startup?.WaitAsync(ct) ?? Task.FromResult(false);
    }

    internal void Abandon()
    {
        // Teardown remains registered while foreign code runs, even if the startup caller has timed out.
        _ = DisposeAsync().AsTask().ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    public ValueTask DisposeAsync()
    {
        binding.Invalidate();
#pragma warning disable VSTHRD003 // Both the startup task and its drain run on the thread pool.
        lock (_gate) return new ValueTask(_closing ??= Task.Run(() => CloseAsync(_startup)));
#pragma warning restore VSTHRD003
    }

    private async Task CloseAsync(Task<bool>? startup)
    {
        try
        {
            var stopping = StopBindingAsync();
#pragma warning disable VSTHRD003 // Startup is pool-owned and cannot require a UI or coordinator lock.
            if (startup is not null) await Task.WhenAll(stopping, startup).ConfigureAwait(false);
            else await stopping.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        finally
        {
            try
            {
                await host.DrainArchiveCallsAsync().ConfigureAwait(false);
                host.Dispose();
            }
            finally
            {
                plugins.Detach(this);
                IDisposable? registration;
                lock (_gate)
                {
                    registration = _registration;
                    _registration = null;
                }
                registration?.Dispose();
            }
        }
    }

    private async Task StopBindingAsync()
    {
        try { await binding.DisposeAsync().ConfigureAwait(false); }
        catch (AggregateException error) { host.Report(binding.Package.Id, "cancel", error); }
    }

    public Task<Result<PluginValue>> InvokeHookAsync(string hookName, PluginValue arguments, TimeSpan budget, CancellationToken ct) =>
        UnavailableAsync<PluginValue>();
    public Task<Result<PluginValue>> InvokeCallbackAsync(string callbackId, PluginValue arguments, TimeSpan budget, CancellationToken ct) =>
        UnavailableAsync<PluginValue>();
    public Task<Result<bool>> ShouldShowAsync(string slot, PluginValue context, CancellationToken ct) => UnavailableAsync<bool>();
    public Task<Result<Unit>> SendAsync(PluginValue hostMessage, CancellationToken ct) => UnavailableAsync<Unit>();

    private static Task<Result<T>> UnavailableAsync<T>() =>
        Task.FromResult(Result<T>.Fail(new ValidationError("The native add-on uses the managed plug-in API.")));
}
