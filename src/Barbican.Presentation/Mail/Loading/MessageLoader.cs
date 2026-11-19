// Mirrors: Rust msgload.rs (tag rust-final): open :100, prefill :175, mark_read :202, present :221, prefetch :320, prefetch_top :351; mailview.rs
// show/ready (the header changes right before the page is swapped in). Bulwark components/email/email-viewer.tsx, stores/email-store.ts
// beginOpenEmail/finishOpenEmail :3990-:4010 (onEmailOpen/onEmailClose), lib/email-prefetch.ts, components/email/reader-open-failure.tsx.
// Spec B-SYNC-022..029 (reader), PORT-PLAN.md §4.5 (no flicker). Stalwart crates/jmap/src/email/get.rs (bodies, isTruncated). Owner body.
//
// The rules (msgload.rs module comment):
// - the message on screen (header and body) stays until the next one is ready, then both change together: the header is applied in the
//   body view's ready callback, in the same UI-thread turn as the reveal;
// - when loading takes over 150 ms the thin loading bar appears, the header shows the selected row and the old body fades (the reader
//   never shows one message's header over another's body); the timer starts once per burst, key repeat keeps it;
// - bodies and conversations come from the body cache, and the neighbours in the list are prefetched in the direction of travel;
// - from an empty reader the header is filled from the list row straight away;
// - a superseded open is cancelled and its late result ignored (sequence number + CancellationTokenSource);
// - mark-as-read is never sent in M1: IMailActions.CanWrite is false (ReadOnlyMailActions); the markAsReadDelay logic stays ported.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Body.Hooks;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>Opens messages in the reader without flicker (Rust msgload.rs); replaces mailcore's header-only placeholder.</summary>
public sealed class MessageLoader : IMessageOpener, IConversationOpener, IDisposable
{
    /// <summary>After this long the loading bar shows, the header follows the selection and the old body fades.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromMilliseconds(150);

    /// <summary>How long an open waits for the prefetch of its own message before asking itself.</summary>
    public static readonly TimeSpan PrefetchWait = TimeSpan.FromSeconds(5);

    private readonly ReaderViewModel _reader;
    private readonly IActiveMail _active;
    private readonly MessageFetcher _fetcher;
    private readonly MessagePrefetcher _prefetcher;
    private readonly IBodyPresenter _presenter;
    private readonly IMessageNeighbours _neighbours;
    private readonly IMailActions _actions;
    private readonly IHookBus _hooks;
    private readonly IDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly IPrefsStore _prefs;
    private readonly Func<ReaderOptions> _options;
    private readonly ReaderDepth? _depth;
    private long _seq;
    private int _opens;
    private int _cachedOpens;
    private CancellationTokenSource? _openCts;
    private MessageRowViewModel? _currentRow;
    private string? _shownId;
    private ConversationThread? _thread;
    private bool _waiting;
    private bool _loadingShown;
    private ITimer? _slowTimer;
    private ITimer? _markTimer;
    private long _openedAt;
    private string _zoneSetting = string.Empty;
    private int _disposed;

    // banners of a slow source (ReaderDepth.BannerBudget) that arrived before their page was revealed: OnReady of that open takes them
    private readonly Lock _lateGate = new();
    private long _readySeq = -1;
    private long _lateSeq = -1;
    private IReadOnlyList<Reader.ReaderBanner>? _late;

    public MessageLoader(
        ReaderViewModel reader,
        IActiveMail active,
        MessageFetcher fetcher,
        MessagePrefetcher prefetcher,
        IBodyPresenter presenter,
        IMessageNeighbours neighbours,
        IMailActions actions,
        IHookBus hooks,
        IDispatcher dispatcher,
        TimeProvider time,
        IPrefsStore prefs,
        Func<ReaderOptions> options,
        ReaderDepth? depth = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(fetcher);
        ArgumentNullException.ThrowIfNull(prefetcher);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(neighbours);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(options);
        _reader = reader;
        _active = active;
        _fetcher = fetcher;
        _prefetcher = prefetcher;
        _presenter = presenter;
        _neighbours = neighbours;
        _actions = actions;
        _hooks = hooks;
        _dispatcher = dispatcher;
        _time = time;
        _prefs = prefs;
        _options = options;
        _depth = depth;
        _zoneSetting = _prefs.Current.Str("timeZone", "auto");
        _prefs.Changed += OnZonePrefsChanged;
        _active.Changed += OnAccountChanged;
        _neighbours.ListLoaded += OnListLoaded;
    }

    /// <summary>A message is being fetched for the reader (it still shows the previous one).</summary>
    public bool IsWaiting => _waiting;

    /// <summary>The slow state is on: loading bar, header of the selection, old body dimmed.</summary>
    public bool IsLoadingShown => _loadingShown;

    /// <summary>The message the reader shows (header and body applied), or null.</summary>
    public string? ShownId => _shownId;

    /// <summary>From the last open to its header and body going on screen together (the feel probe of the hidden runs), or null.</summary>
    public TimeSpan? LastOpenToShown { get; private set; }

    /// <summary>Opens since start; the ones that were shown from the cache without waiting.</summary>
    public (int Opens, int CachedOpens) OpenCounts => (_opens, _cachedOpens);

    /// <summary>The prefetcher (tests read its travel state).</summary>
    public MessagePrefetcher Prefetcher => _prefetcher;

    public void Open(MessageRowViewModel row, OpenReason reason, ListDirection direction)
    {
        ArgumentNullException.ThrowIfNull(row);
        OpenCore(row);
    }

    public bool OpenConversationMessage(string emailId)
    {
        var summary = _thread?.Emails.FirstOrDefault(e => string.Equals(e.Id, emailId, StringComparison.Ordinal));
        if (summary is null || _thread is null)
        {
            return false;
        }

        var threadId = _thread.ThreadId;
        _hooks.Fire(HookNames.OnThreadExpand, () => new ThreadExpandPayload(threadId, [emailId], true));
        OpenCore(new MessageRowViewModel(summary));
        return true;
    }

    public void Close()
    {
        Supersede();
        _currentRow = null;
        _thread = null;
        EndWaiting();
        _presenter.Clear();
        _reader.Clear();
        var previous = _shownId;
        _shownId = null;
        AnnounceClose(previous);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _prefs.Changed -= OnZonePrefsChanged;
        _active.Changed -= OnAccountChanged;
        _neighbours.ListLoaded -= OnListLoaded;
        _openCts?.Cancel();
        _openCts?.Dispose();
        _slowTimer?.Dispose();
        _markTimer?.Dispose();
        _prefetcher.Dispose();
    }

    private void OpenCore(MessageRowViewModel row)
    {
        var seq = Supersede();
        _openedAt = _time.GetTimestamp();
        _opens++;
        var ct = _openCts!.Token;
        var id = row.Id;
        _currentRow = row;
        var account = _active.AccountKey;
        var mail = _active.Mail;
        if (account is null || mail is null)
        {
            _reader.Apply(ReaderSnapshots.Prefill(row, _options()));
            return;
        }

        var prefs = _prefs.Current;
        var delay = MessageLoadRules.MarkAsReadDelay(prefs);
        var threading = MessageLoadRules.Threading(prefs);

        // read state before anything marks the row read (the server would still have to be told)
        var unseen = MessageLoadRules.WasUnseen(row.Summary.Keywords, _fetcher.CachedBody(account, id)?.Keywords);
        if (_fetcher.Cached(account, id, threading) is { } hit)
        {
            EndWaiting();
            _cachedOpens++;
            Present(row, hit, seq, unseen, delay, new PrefetchContext(account, mail, threading));
            return;
        }

        // a page still loading for the message before must not appear under this one
        _presenter.CancelPending();
        _waiting = true;
        if (_reader.BodyState is BodyState.Empty or BodyState.Failed or BodyState.Loading || _loadingShown)
        {
            // an empty reader, or one already showing the loading state: the row's header now
            _reader.Apply(ReaderSnapshots.Prefill(row, _options()));
        }
        else
        {
            // started once per burst (key repeat keeps it), not restarted on every key
            _slowTimer ??= _time.CreateTimer(_ => _dispatcher.Post(OnSlow), null, SlowAfter, Timeout.InfiniteTimeSpan);
        }

        var context = new PrefetchContext(account, mail, threading);
        Prefetch(id, context);
        _ = LoadAsync(row, seq, unseen, delay, context, ct);
    }

    private async Task LoadAsync(MessageRowViewModel row, long seq, bool? unseen, long delay, PrefetchContext context, CancellationToken ct)
    {
        Result<FetchedMessage> result;
        try
        {
            // already on its way (prefetched): wait for that instead of asking twice
            if (_prefetcher.TryGetPending(row.Id, out var pending))
            {
                try
                {
                    await pending.WaitAsync(PrefetchWait, _time, ct).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // ask ourselves
                }
#pragma warning disable CA1031 // a failed prefetch only means the open fetches the message itself
                catch (Exception) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
                {
                    // a prefetch that failed or was cancelled (the direction of travel turned, which cancels its batch) is not this open's
                    // cancellation: the open fetches the message itself instead of being dropped with the reader left waiting
                }
            }

            result = await _fetcher.FetchAsync(context.AccountKey, context.Mail, row.Id, context.WantThread, prefetch: false, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
#pragma warning disable CA1031 // anything that goes wrong on the way is shown as a failed open, never a crash
        catch (Exception e)
#pragma warning restore CA1031
        {
            result = Result<FetchedMessage>.Fail(new TransportError(e.Message, false));
        }

        _dispatcher.Post(() =>
        {
            // another message was opened meanwhile: this result is not to be shown
            if (seq != _seq)
            {
                return;
            }

            if (result.IsOk && result.Value is { } fetched)
            {
                Present(row, fetched, seq, unseen, delay, context);
            }
            else
            {
                Fail(row, result.Error?.Message ?? "unknown error");
            }
        });
    }

    // Show a loaded message: the body goes into the view hidden, then header and body change together.
    private void Present(MessageRowViewModel row, FetchedMessage fetched, long seq, bool? unseen, long delay, PrefetchContext context)
    {
        var body = fetched.Body;
        var id = row.Id;

        // the list's copy has the current keywords (a cached body may predate a flag or a read)
        var shown = body with { Keywords = row.Summary.Keywords };
        // a unified row is judged by the account that holds it (its server for the trace block, its folders, its pin), Fable review 3 R3-01
        var (owner, source, ownerMail) = _fetcher.OwnerOf(context.AccountKey, context.Mail, id);
        var options = _options() with { ServerHost = AuthServTrust.ServerHostOf(owner) };
        if (_depth is not null)
        {
            options = _depth.Shape(options, owner, ownerMail, source);
        }

        var snapshot = ReaderSnapshots.FromBody(shown, fetched.Thread, options) with { ListAccountKey = context.AccountKey };
        _depth?.Learn(owner, snapshot.LearnedAuthServ);
        ScheduleMarkRead(id, unseen ?? !body.Keywords.Seen, delay);
        var content = BodyRequest.ContentOf(body);
        if (_depth is { HasBanners: true })
        {
            _ = BannersThenShowAsync(id, body, fetched.Thread, snapshot, content, seq, context);
            return;
        }

        if (!_hooks.HasHandlers(HookNames.OnRenderEmailBody))
        {
            Show(id, body, fetched.Thread, snapshot, content, blockRemote: false, seq, context);
            return;
        }

        _ = TransformThenShowAsync(id, body, fetched.Thread, snapshot, content, seq, context);
    }

    // the banners go into the snapshot before the page is built, so they are applied with the header in the reveal's UI turn (no late pop-in)
    private async Task BannersThenShowAsync(string id, EmailBody body, ConversationThread? thread, ReaderSnapshot snapshot, MailContent content, long seq, PrefetchContext context)
    {
        var (banners, late) = await _depth!.BannersAsync(snapshot, _time, _openCts?.Token ?? CancellationToken.None).ConfigureAwait(true);
        if (seq != _seq)
        {
            return;
        }

        if (late is not null)
        {
            _ = LateBannersAsync(id, seq, late);
        }

        var withBanners = snapshot with { Banners = banners };
        if (_hooks.HasHandlers(HookNames.OnRenderEmailBody))
        {
            await TransformThenShowAsync(id, body, thread, withBanners, content, seq, context).ConfigureAwait(true);
            return;
        }

        _dispatcher.Post(() =>
        {
            if (seq == _seq)
            {
                Show(id, body, thread, withBanners, content, blockRemote: false, seq, context);
            }
        });
    }

    // A slow source's banners (an invitation waits on CalendarEvent/parse): before the reveal they go with the page, after it they join the stack
    // of the message on screen in one change.
    private async Task LateBannersAsync(string id, long seq, Task<IReadOnlyList<Reader.ReaderBanner>> late)
    {
#pragma warning disable VSTHRD003 // ReaderDepth.BannersAsync started it for this open; it never faults and needs no context (the result is posted)
        var more = await late.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        if (more.Count == 0)
        {
            return;
        }

        _dispatcher.Post(() =>
        {
            if (seq != _seq)
            {
                return;
            }

            lock (_lateGate)
            {
                if (_readySeq != seq)
                {
                    (_lateSeq, _late) = (seq, more);
                    return;
                }
            }

            _reader.AddBanners(id, more);
        });
    }

    // onRenderEmailBody: a plugin may take the body over (decrypted mail) before the page is built
    private async Task TransformThenShowAsync(string id, EmailBody body, ConversationThread? thread, ReaderSnapshot snapshot, MailContent content, long seq, PrefetchContext context)
    {
        RenderableBody shaped;
        try
        {
            shaped = await _hooks.TransformAsync(HookNames.OnRenderEmailBody, new RenderableBody(body), CancellationToken.None).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // a failing plugin falls through to the message's own body (Bulwark email-viewer.tsx:2783)
        catch (Exception)
#pragma warning restore CA1031
        {
            shaped = new RenderableBody(body);
        }

        // a plugin's HTML gets the same sanitizer as a fetched body (Fable F-03), off the UI thread
        if (shaped.HandledBy is not null && shaped.Html is { } pluginHtml)
        {
            var clean = await Task.Run(() => MailSanitizer.Clean(pluginHtml, out _)).ConfigureAwait(true);
            shaped = shaped with { Html = clean };
        }

        _dispatcher.Post(() =>
        {
            if (seq != _seq)
            {
                return;
            }

            var handled = shaped.HandledBy is not null && (shaped.Html is not null || shaped.Text is not null);
            var replaced = handled ? new MailContent(shaped.Html, shaped.Text ?? string.Empty) : content;
            Show(id, body, thread, snapshot, replaced, handled && shaped.BlockExternalContent, seq, context);
        });
    }

    private void Show(string id, EmailBody body, ConversationThread? thread, ReaderSnapshot snapshot, MailContent content, bool blockRemote, long seq, PrefetchContext context)
    {
        // onEmailContentRender fires once per opened message (Bulwark email-viewer.tsx:3937 contentRenderedForRef), not again when the
        // message on screen is opened again (Enter)
        var opened = false;
        var request = new BodyRequest(
            id,
            content,
            blockRemote,
            OnReady: () =>
            {
                // another message was opened meanwhile: this page is not to be shown
                if (seq != _seq)
                {
                    return false;
                }

                EndWaiting();
                lock (_lateGate)
                {
                    _readySeq = seq;
                    if (_lateSeq == seq && _late is { Count: > 0 } late)
                    {
                        snapshot = snapshot with { Banners = [.. snapshot.Banners, .. late.Where(b => !snapshot.Banners.Any(x => string.Equals(x.Id, b.Id, StringComparison.Ordinal)))] };
                        _late = null;
                    }
                }

                var previous = _shownId;
                _shownId = id;
                _thread = thread;
                var currentZone = ReaderSnapshots.FromBody(body, thread, _options());
                snapshot = snapshot with { Header = snapshot.Header with { Date = currentZone.Header.Date, CompactDate = currentZone.Header.CompactDate }, Conversation = currentZone.Conversation };
                _reader.Apply(snapshot);
                LastOpenToShown = _time.GetElapsedTime(_openedAt);
                if (!string.Equals(previous, id, StringComparison.Ordinal))
                {
                    opened = true;
                    AnnounceClose(previous);
                    _hooks.Fire(HookNames.OnEmailOpen, () => EmailReadView.Of(body));
                }

                Prefetch(id, context);
                return true;
            },
            OnShown: () =>
            {
                // hasExternalContent: the body refers to remote content, blocked or loaded (Bulwark email-viewer.tsx:3944-3951)
                if (opened)
                {
                    _hooks.Fire(HookNames.OnEmailContentRender, () => new EmailContentRenderPayload(id, content.Html is not null, content.HasRemoteContent));
                }
            })
        {
            TrustedSender = _depth?.Trusted(body) ?? false,
        };
        _presenter.Show(request);
    }

    // The fetch failed: nothing of the previous message may stay (header, body).
    private void Fail(MessageRowViewModel row, string error)
    {
        EndWaiting();
        _presenter.Clear();
        var previous = _shownId;
        _shownId = null;
        _thread = null;
        _reader.Apply(ReaderSnapshots.Prefill(row, _options()) with { BodyState = BodyState.Failed, Error = "Could not load message: " + error });
        AnnounceClose(previous);
    }

    private void OnSlow()
    {
        _slowTimer?.Dispose();
        _slowTimer = null;
        if (!_waiting || _currentRow is null)
        {
            return;
        }

        _loadingShown = true;
        _presenter.SetLoading(true);
        _reader.Apply(ReaderSnapshots.Prefill(_currentRow, _options()));
        _presenter.SetDimmed(true);
    }

    // Bulwark markAsReadDelay: 0 = at once, N ms = if still open then, -1 = never. Never runs in M1 (CanWrite is false).
    private void ScheduleMarkRead(string id, bool unseen, long delay)
    {
        _markTimer?.Dispose();
        _markTimer = null;
        if (!_actions.CanWrite || delay < 0 || !unseen)
        {
            return;
        }

        if (delay == 0)
        {
            _ = _actions.MarkReadAsync(id, CancellationToken.None);
            return;
        }

        _markTimer = _time.CreateTimer(
            _ => _dispatcher.Post(() =>
            {
                if (string.Equals(_currentRow?.Id, id, StringComparison.Ordinal))
                {
                    _ = _actions.MarkReadAsync(id, CancellationToken.None);
                }
            }),
            null,
            TimeSpan.FromMilliseconds(delay),
            Timeout.InfiniteTimeSpan);
    }

    private void Prefetch(string id, PrefetchContext context) => _prefetcher.Around(id, _neighbours.Rows, _reader.Conversation, context);

    private long Supersede()
    {
        var seq = ++_seq;
        _openCts?.Cancel();
        _openCts?.Dispose();
        _openCts = new CancellationTokenSource();
        _markTimer?.Dispose();
        _markTimer = null;
        return seq;
    }

    private void EndWaiting()
    {
        _waiting = false;
        _slowTimer?.Dispose();
        _slowTimer = null;
        if (_loadingShown)
        {
            _loadingShown = false;
            _presenter.SetLoading(false);
        }
    }

    private void AnnounceClose(string? closed)
    {
        if (closed is not null)
        {
            _hooks.Fire(HookNames.OnEmailClose, () => new EmailIdPayload(closed));
        }
    }

    private void OnListLoaded(object? sender, EventArgs e)
    {
        if (_active.AccountKey is { } account && _active.Mail is { } mail)
        {
            _prefetcher.Top(_neighbours.Rows, new PrefetchContext(account, mail, MessageLoadRules.Threading(_prefs.Current)));
        }
    }

    private void OnZonePrefsChanged(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var setting = _prefs.Current.Str("timeZone", "auto");
        if (string.Equals(setting, _zoneSetting, StringComparison.Ordinal)) return;
        _zoneSetting = setting;
        var current = _reader.Current;
        if (current.Body is { } body)
        {
            var fresh = ReaderSnapshots.FromBody(body, _thread, _options());
            _reader.Apply(current with { Header = current.Header with { Date = fresh.Header.Date, CompactDate = fresh.Header.CompactDate }, Conversation = fresh.Conversation });
        }
        else if (_currentRow is { } row && string.Equals(row.Id, current.EmailId, StringComparison.Ordinal))
        {
            var fresh = ReaderSnapshots.Prefill(row, _options());
            _reader.Apply(current with { Header = current.Header with { Date = fresh.Header.Date, CompactDate = fresh.Header.CompactDate } });
        }
    });

    private void OnAccountChanged(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        _prefetcher.CancelAll();
        Close();
    });
}
