// Mirrors: Rust main.rs:532 schedule_refresh (pushes coalesced for 700 ms, then: Mailbox/Email/Thread -> the folder tree by
// Mailbox/changes, Email/Thread -> the open list by list_delta, Email/EmailDelivery -> new-mail check), mail.rs:254 load_mailboxes
// (delta from the kept state, else a full read), mail.rs:759 sync (apply the delta; no delta -> reload the list); Bulwark
// stores/email-store.ts:1301 onNewEmailReceived, :1371 onSyncStateChange (overlapping runs share one syncing/idle cycle),
// lib/mail-push-listener.ts. Owner jmap.

using System.Threading.Channels;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Sync;

/// <summary>The push and delta engine of one account.</summary>
public sealed partial class MailSync : IMailSync, IDisposable
{
    /// <summary>The wait that gathers a burst of pushes into one sync.</summary>
    public static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(700);

    private readonly MailService _service;
    private readonly IJmapClient _client;
    private readonly IHookBus _hooks;
    private readonly TimeProvider _time;
    private readonly Channel<MailSyncEvent> _events = Channel.CreateUnbounded<MailSyncEvent>(new UnboundedChannelOptions { SingleReader = false });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _lock = new();
    private readonly NewMailWatcher _newMail;
    private WatchedList? _watched;
    private int _runs;
    private string? _runError;

    public MailSync(MailService service, IHookBus hooks, TimeProvider time, PushBackoff? backoff = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(time);
        _service = service;
        _client = service.Client;
        _hooks = hooks;
        _time = time;
        _backoff = backoff ?? PushBackoff.WithRandomJitter();
        _newMail = new NewMailWatcher(service);
        _timer = time.CreateTimer(_ => OnCoalesced(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc/>
    public ChannelReader<MailSyncEvent> Events => _events.Reader;

    /// <summary>The new-mail check (Bulwark pushNotifyInboxOnly lives here).</summary>
    public NewMailWatcher NewMail => _newMail;

    /// <inheritdoc/>
    public void Watch(ListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_lock)
        {
            _watched = new WatchedList(query, null);
        }
    }

    /// <inheritdoc/>
    public void Watch(ListQuery query, ListPage loaded)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(loaded);
        lock (_lock)
        {
            _watched = new WatchedList(query, ListStates.Of(loaded));
        }
    }

    /// <summary>Syncs what changed for the given data types now (the coalesced push calls this).</summary>
    public async Task SyncNowAsync(IReadOnlySet<string> types, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(types);
        // every begun run ends, also one cancelled while it waits for the gate or the "syncing" hook: otherwise the
        // syncing/idle cycle of onSyncStateChange would stay "syncing" for good (Bulwark stores/email-store.ts:1371)
        string? error = null;
        var entered = false;
        var first = BeginRun();
        List<EmailSummary> hear = [];
        try
        {
            if (first)
            {
                await NotifyAsync(HookNames.OnSyncStateChange, SyncStatePayload.Syncing, ct).ConfigureAwait(false);
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            entered = true;
            error = await RunTypesAsync(types, hear, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the top of a background run: whatever went wrong is reported through onSyncStateChange, never left on a faulted task (Fable F-15)
        catch (Exception e)
#pragma warning restore CA1031
        {
            error = e.Message;
        }
        finally
        {
            if (entered)
            {
                _gate.Release();
            }

            // outside the gate: a plugin that is slow to hear of new mail must not hold up the next sync (Fable F-15)
            await TellNewMailAsync(hear).ConfigureAwait(false);

            await EndRunAsync(error, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _timer.Dispose();
        _gate.Dispose();
        _events.Writer.TryComplete();
    }

    private async Task<string?> RunTypesAsync(IReadOnlySet<string> types, List<EmailSummary> hear, CancellationToken ct)
    {
        bool Has(string t) => types.Contains(t);
        JmapError? error = null;
        if (Has("Mailbox") || Has("Email") || Has("Thread"))
        {
            error ??= await SyncMailboxesAsync(ct).ConfigureAwait(false);
        }

        if (Has("Email") || Has("Thread"))
        {
            error ??= await SyncListAsync(ct).ConfigureAwait(false);
        }

        if (Has("Email") || Has("EmailDelivery"))
        {
            error ??= await CheckNewMailAsync(hear, ct).ConfigureAwait(false);
        }

        return error?.Message;
    }

    /// <summary>The folder tree: a Mailbox/changes delta from the kept state, else a full read.</summary>
    private async Task<JmapError?> SyncMailboxesAsync(CancellationToken ct)
    {
        if (_service.LastMailboxes is { State: { } since } last)
        {
            var delta = await MailboxDeltaFetcher.FetchAsync(_client, since, last.List, ct).ConfigureAwait(false);
            if (delta is { IsOk: true, Value: { } d })
            {
                _service.Remember(d.List, d.State);
                Emit(new MailboxesChangedEvent(MailService.Tree(d.List, d.State)));
                return null;
            }
        }

        var full = await _service.GetMailboxObjectsAsync(ct).ConfigureAwait(false);
        if (full.IsOk)
        {
            Emit(new MailboxesChangedEvent(MailService.Tree(full.Value.List, full.Value.State)));
        }

        return full.Error;
    }

    /// <summary>The open list: a delta from its states; when none can be had the list is reset (the view reads it again).</summary>
    private async Task<JmapError?> SyncListAsync(CancellationToken ct)
    {
        WatchedList? w;
        lock (_lock)
        {
            w = _watched;
        }

        if (w is null)
        {
            return null;
        }

        var delta = w.States is null ? Result<ListDelta?>.Ok(null) : await ListDeltaFetcher.FetchAsync(_client, ListFilters.For(w.Query), w.States, ct).ConfigureAwait(false);
        lock (_lock)
        {
            if (!ReferenceEquals(_watched, w))
            {
                // the view moved to another list meanwhile: its own states stand
                return null;
            }

            _watched = delta is { IsOk: true, Value: { } d } ? w with { States = ListStates.After(d) } : w with { States = null };
        }

        if (delta is { IsOk: true, Value: { } applied })
        {
            Emit(new ListDeltaEvent(applied) { Query = w.Query });
            Emit(new EmailsChangedEvent(applied.Emails, applied.Removed, applied.DestroyedThreads));
            return null;
        }

        Emit(new ListResetEvent { Query = w.Query });
        return delta.Error;
    }

    private async Task<JmapError?> CheckNewMailAsync(List<EmailSummary> hear, CancellationToken ct)
    {
        var fresh = await _newMail.CheckAsync(ct).ConfigureAwait(false);
        if (!fresh.IsOk || fresh.Value!.Count == 0)
        {
            return fresh.Error;
        }

        Emit(new NewMailEvent(fresh.Value));
        if (_hooks.HasHandlers(HookNames.OnNewEmailReceived)
            && await _service.GetRoleIdsAsync(ct).ConfigureAwait(false) is { IsOk: true } roles
            && roles.Value!.TryGetValue("inbox", out var inbox))
        {
            // plugins hear of new mail in the inbox only, never of a draft (Bulwark stores/email-store.ts:1346)
            hear.AddRange(fresh.Value.Where(e => e.MailboxIds.ContainsKey(inbox) && !e.Keywords.IsSet("$draft")));
        }

        return null;
    }

    private async Task TellNewMailAsync(List<EmailSummary> hear)
    {
        foreach (var email in hear)
        {
            await NotifyAsync(HookNames.OnNewEmailReceived, email, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Counts a run in; true for the first of overlapping runs (the one that reports "syncing").</summary>
    private bool BeginRun()
    {
        lock (_lock)
        {
            if (++_runs > 1)
            {
                return false;
            }

            _runError = null;
            return true;
        }
    }

    private async Task EndRunAsync(string? error, CancellationToken ct)
    {
        string? failed;
        lock (_lock)
        {
            _runError ??= error;
            if (--_runs > 0)
            {
                return;
            }

            failed = _runError;
        }

        await NotifyAsync(HookNames.OnSyncStateChange, failed is null ? SyncStatePayload.Idle : new SyncStatePayload("error", failed), ct).ConfigureAwait(false);
    }

    private async Task NotifyAsync<T>(string hook, T payload, CancellationToken ct)
    {
        if (!_hooks.HasHandlers(hook))
        {
            return;
        }

        try
        {
            await _hooks.NotifyAsync(hook, payload, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a hook is the plugin boundary: whatever it throws must not fault the sync (Fable F-15)
        catch (Exception)
#pragma warning restore CA1031
        {
            // the bus reports handler failures itself
        }
    }

    private void Emit(MailSyncEvent e) => _events.Writer.TryWrite(e);

    private sealed record WatchedList(ListQuery Query, ListStates? States);
}
