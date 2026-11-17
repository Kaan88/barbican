// Mirrors: Rust main.rs push wiring (one stream feeds the list, the folders and the body cache); M1-plan §4.1 (IMailSync.Events is one channel
// with one reader). Owner mailcore.

using Barbican.Core.Mail.Model;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;

namespace Barbican.Presentation.Mail;

/// <summary>
/// Reads the active account's sync events once and hands each to every subscriber on the UI thread (a channel has one reader, the list and the
/// sidebar both need the events). Also runs the sync engine of the active account; follows account switches.
/// </summary>
public sealed class MailEventHub : IStartupTask, IDisposable
{
    private readonly IActiveMail _active;
    private readonly IDispatcher _dispatcher;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cts;

    public MailEventHub(IActiveMail active, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _active = active;
        _dispatcher = dispatcher;
    }

    /// <summary>An event arrived; raised on the UI thread.</summary>
    public event EventHandler<MailSyncEventArgs>? EventReceived;

    /// <summary>Start reading the active account now and again after every switch.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _active.Changed += OnChanged;
        Attach();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _active.Changed -= OnChanged;
        Stop();
    }

    /// <summary>Hands an event to the subscribers as if the sync engine had sent it (tests and the headless scenario).</summary>
    public void Publish(MailSyncEvent e) => Publish(e, _active.AccountKey);

    /// <summary>Hands an event to the subscribers as an event of <paramref name="accountKey"/> (the account whose engine produced it).</summary>
    public void Publish(MailSyncEvent e, string? accountKey) => _dispatcher.Post(() => EventReceived?.Invoke(this, new MailSyncEventArgs(e, accountKey)));

    private void OnChanged(object? sender, EventArgs e) => Attach();

    private void Stop()
    {
        CancellationTokenSource? old;
        lock (_gate)
        {
            old = _cts;
            _cts = null;
        }

        if (old is not null)
        {
            old.Cancel();
            old.Dispose();
        }
    }

    private void Attach()
    {
        Stop();
        var sync = _active.Sync;
        if (sync is null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            _cts = cts;
        }

        var ct = cts.Token;
        var key = _active.AccountKey;
        _ = Task.Run(() => RunAsync(sync, key, ct), ct);
    }

    private async Task RunAsync(Core.Mail.IMailSync sync, string? accountKey, CancellationToken ct)
    {
        var engine = sync.RunAsync(ct);
        try
        {
            await foreach (var e in sync.Events.ReadAllAsync(ct).ConfigureAwait(false))
            {
                Publish(e, accountKey);
            }
        }
        catch (OperationCanceledException)
        {
            // switched account or shut down
        }

        try
        {
            await engine.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // the engine stops with the token
        }
    }
}
