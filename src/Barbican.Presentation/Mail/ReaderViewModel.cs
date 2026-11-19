// Mirrors: M1-plan §4.3 (Header, Conversation strip, Attachments, AuthResults, BodyState, Apply(ReaderSnapshot)); Rust msgload.rs (header and body
// change together, B-SYNC-022; a failed fetch leaves nothing of the previous message, B-SYNC-029); spec I-194..I-233; M2 banners (reader). Owner reader (M2).

using CommunityToolkit.Mvvm.ComponentModel;
using Barbican.Presentation.Common;
using Barbican.Presentation.Reader;
using Barbican.Core.Mail.VerificationCode;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Mail;

/// <summary>The reader. <see cref="Apply"/> changes everything in one call, so the header and the body of one message never show apart.</summary>
public sealed class ReaderViewModel : ObservableObject, IDisposable
{
    private ReaderSnapshot _current = ReaderSnapshot.Empty;
    private readonly IPrefsStore? _prefs;
    private readonly IDispatcher? _dispatcher;
    private readonly VerificationCodeBannerSource? _code;
    private readonly TimeProvider _time;
    private ITimer? _codeExpiry;
    private bool _disposed;
    private bool _showOlderConversationHeaders;

    public ReaderViewModel(IPrefsStore? prefs = null, IVerificationCodeFinder? finder = null, TimeProvider? time = null, IDispatcher? dispatcher = null)
    {
        _prefs = prefs;
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
        _code = finder is null ? null : new VerificationCodeBannerSource(finder, _time, prefs);
        if (_prefs is not null) _prefs.Changed += OnPrefsChanged;
    }

    public void Dispose()
    {
        _disposed = true;
        _codeExpiry?.Dispose();
        if (_prefs is not null) _prefs.Changed -= OnPrefsChanged;
    }

    private void OnPrefsChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess) _dispatcher.Post(RefreshCode);
        else RefreshCode();
    }

    private void RefreshCode()
    {
        if (!_disposed) Apply(_current);
    }

    private void ScheduleCodeExpiry(DateTimeOffset? received)
    {
        if (received is null)
        {
            _codeExpiry?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        _codeExpiry ??= _time.CreateTimer(_ => OnPrefsChanged(this, EventArgs.Empty), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var due = received.Value + VerificationCodeBannerSource.Fresh + TimeSpan.FromMilliseconds(1) - _time.GetUtcNow();
        _codeExpiry.Change(due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
    }

    private ReaderSnapshot WithCode(ReaderSnapshot snapshot)
    {
        if (_code is null) return snapshot;
        var banners = snapshot.Banners.Where(b => !string.Equals(b.Id, VerificationCodeBannerSource.Id, StringComparison.Ordinal)).ToList();
        if (_code.CodeOf(snapshot) is { } code)
        {
            banners.Add(new ReaderBanner(VerificationCodeBannerSource.Id, $"Verification code {code}", null, false, [(VerificationCodeBannerSource.Copy, "Copy code")]));
            ScheduleCodeExpiry(snapshot.Body?.ReceivedAt);
        }
        else ScheduleCodeExpiry(null);
        return snapshot with { Banners = banners };
    }

    /// <summary>The snapshot on screen.</summary>
    public ReaderSnapshot Current => _current;

    public string EmailId => _current.EmailId;

    public ReaderHeader Header => _current.Header;

    public BodyState BodyState => _current.BodyState;

    public bool IsEmpty => _current.BodyState == BodyState.Empty;

    public string AttachmentSummary => _current.AttachmentSummary;

    public bool ShowAttachmentCards => _current.ShowAttachmentCards;

    public IReadOnlyList<AuthChip> AuthChips => _current.AuthChips;

    public AuthVerdict AuthVerdict => _current.AuthVerdict;

    public string? Error => _current.Error;

    /// <summary>The conversation strip; the collection is never replaced, only synced in place.</summary>
    public BulkObservableCollection<ConversationItem> Conversation { get; } = [];

    /// <summary>Collapsed headers only; the selected message keeps the reader's single body surface.</summary>
    public BulkObservableCollection<ConversationItem> ConversationHeaders { get; } = [];

    public int HiddenConversationCount { get; private set; }

    public string OlderConversationLabel => FormattableString.Invariant($"Show {HiddenConversationCount} older messages");

    public void ShowOlderConversationHeaders()
    {
        _showOlderConversationHeaders = true;
        SyncConversationHeaders(_current.Conversation);
    }

    private void SyncConversationHeaders(IReadOnlyList<ConversationItem> items)
    {
        var current = 0;
        while (current < items.Count && !items[current].IsCurrent) current++;
        var hidden = !_showOlderConversationHeaders && current < items.Count ? Math.Max(0, current - 2) : 0;
        ListSync.SyncBy(ConversationHeaders, items.Skip(hidden).Where(c => !c.IsCurrent).ToArray(), c => c.Id, EqualityComparer<ConversationItem>.Default.Equals);
        if (HiddenConversationCount != hidden)
        {
            HiddenConversationCount = hidden;
            OnPropertyChanged(nameof(HiddenConversationCount));
            OnPropertyChanged(nameof(OlderConversationLabel));
        }
    }

    /// <summary>The attachment tiles; synced in place so a thumbnail arriving touches one tile.</summary>
    public BulkObservableCollection<AttachmentCard> Attachments { get; } = [];

    /// <summary>The banners above the body; synced in place with the snapshot (same turn as the reveal).</summary>
    public BulkObservableCollection<Reader.ReaderBanner> Banners { get; } = [];

    /// <summary>Takes over a prepared snapshot: every property and both collections change here, then the notifications go out together.</summary>
    public void Apply(ReaderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var previous = _current;
        snapshot = WithCode(snapshot);
        _current = snapshot;
        var emailChanged = !string.Equals(previous.EmailId, snapshot.EmailId, StringComparison.Ordinal);
        if (emailChanged || snapshot.Conversation.Count == 0) _showOlderConversationHeaders = false;
        ListSync.SyncBy(Conversation, snapshot.Conversation, c => c.Id, EqualityComparer<ConversationItem>.Default.Equals);
        SyncConversationHeaders(snapshot.Conversation);
        ListSync.SyncBy(Banners, snapshot.Banners, b => b.Id, EqualityComparer<Reader.ReaderBanner>.Default.Equals);
        ListSync.SyncBy(Attachments, snapshot.Attachments, a => a.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), AttachmentCardsEqual);
        OnPropertyChanged(nameof(Current));
        if (emailChanged)
        {
            OnPropertyChanged(nameof(EmailId));
        }

        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(BodyState));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AttachmentSummary));
        OnPropertyChanged(nameof(ShowAttachmentCards));
        OnPropertyChanged(nameof(AuthChips));
        OnPropertyChanged(nameof(AuthVerdict));
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>
    /// The open message's row changed (a tag or the star set by an action, a push): the header follows it, nothing else moves and the body is not
    /// fetched again (M2 hotfix 2: a tag added or removed while the message was open did not show in the header).
    /// </summary>
    public void FollowRow(MessageRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_current.BodyState == BodyState.Empty || !string.Equals(row.Id, _current.EmailId, StringComparison.Ordinal))
        {
            return;
        }

        var header = _current.Header;
        if (header.Flagged == row.Flagged && header.Tags.SequenceEqual(row.Tags))
        {
            return;
        }

        _current = _current with { Header = header with { Flagged = row.Flagged, Tags = row.Tags } };
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Header));
    }

    /// <summary>A banner was answered or dismissed: it goes, the rest of the reader stays.</summary>
    public void RemoveBanner(string emailId, string bannerId)
    {
        if (!string.Equals(emailId, _current.EmailId, StringComparison.Ordinal))
        {
            return;
        }

        _current = _current with { Banners = [.. _current.Banners.Where(b => !string.Equals(b.Id, bannerId, StringComparison.Ordinal))] };
        ListSync.SyncBy(Banners, _current.Banners, b => b.Id, EqualityComparer<Reader.ReaderBanner>.Default.Equals);
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>Banners of a slower source join the stack of the message on screen (one change: the body moves down once by their height).</summary>
    public void AddBanners(string emailId, IReadOnlyList<Reader.ReaderBanner> banners)
    {
        ArgumentNullException.ThrowIfNull(banners);
        if (!string.Equals(emailId, _current.EmailId, StringComparison.Ordinal))
        {
            return;
        }

        var expectedCode = _code?.CodeOf(_current);
        var fresh = banners.Where(b => (!string.Equals(b.Id, VerificationCodeBannerSource.Id, StringComparison.Ordinal)
                || _code is null || (expectedCode is not null && string.Equals(b.Title, $"Verification code {expectedCode}", StringComparison.Ordinal)))
            && !_current.Banners.Any(x => string.Equals(x.Id, b.Id, StringComparison.Ordinal))).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        _current = _current with { Banners = [.. _current.Banners, .. fresh] };
        ListSync.SyncBy(Banners, _current.Banners, b => b.Id, EqualityComparer<Reader.ReaderBanner>.Default.Equals);
        OnPropertyChanged(nameof(Current));
    }

    /// <summary>Nothing is open any more.</summary>
    public void Clear() => Apply(ReaderSnapshot.Empty);

    /// <summary>A picture arrived for one tile: only that tile changes, the rest of the reader is not touched.</summary>
    public void SetThumbnail(string emailId, int index, Core.Mail.Attachments.ThumbnailImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!string.Equals(emailId, _current.EmailId, StringComparison.Ordinal) || index < 0 || index >= Attachments.Count)
        {
            return;
        }

        Attachments[index] = Attachments[index] with { Thumbnail = image };
    }

    private static bool AttachmentCardsEqual(AttachmentCard a, AttachmentCard b) =>
        a.Index == b.Index
        && string.Equals(a.Stem, b.Stem, StringComparison.Ordinal)
        && string.Equals(a.Kind, b.Kind, StringComparison.Ordinal)
        && string.Equals(a.Size, b.Size, StringComparison.Ordinal)
        && string.Equals(a.BlobId, b.BlobId, StringComparison.Ordinal)
        && ReferenceEquals(a.Thumbnail, b.Thumbnail);
}
