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
    private readonly Dictionary<string, ConversationExpansion> _expanded = new(StringComparer.Ordinal);
    private bool _showOlderConversationHeaders;
    private readonly Dictionary<int, string> _renamed = [];
    private string? _renamedEmailId;

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

    /// <summary>Visible sibling cards. Each expansion owns its body separately from the selected message.</summary>
    public BulkObservableCollection<ConversationItem> ConversationHeaders { get; } = [];

    public int HiddenConversationCount { get; private set; }

    public ConversationExpansion? ToggleConversationCard(string id)
    {
        if (!_expanded.Remove(id) && _current.ListAccountKey is { } key && ConversationHeaders.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal)))
        {
            var expansion = new ConversationExpansion(key, id);
            _expanded.Add(id, expansion);
            SyncConversationHeaders(_current.Conversation);
            return expansion;
        }

        SyncConversationHeaders(_current.Conversation);
        return null;
    }

    public bool Owns(ConversationExpansion card) =>
        card is not null && _expanded.TryGetValue(card.EmailId, out var current) && ReferenceEquals(current, card);

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
        var headers = items.Select((item, index) => (item, index))
            .Where(x => !x.item.IsCurrent && (hidden == 0 || x.index == 0 || x.index >= current - 1))
            .Select(x => hidden > 0 && x.index == current - 1
                ? x.item with { OlderMessageDisclosure = FormattableString.Invariant($"Show {hidden} older messages") }
                : x.item)
            .Select(item => item with { Expansion = _expanded.GetValueOrDefault(item.Id) })
            .ToArray();
        ListSync.SyncBy(ConversationHeaders, headers, c => c.Id, EqualityComparer<ConversationItem>.Default.Equals);
        if (HiddenConversationCount != hidden)
        {
            HiddenConversationCount = hidden;
            OnPropertyChanged(nameof(HiddenConversationCount));
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
        if (emailChanged || !string.Equals(previous.ListAccountKey, snapshot.ListAccountKey, StringComparison.Ordinal) || snapshot.Conversation.Count == 0)
        {
            _showOlderConversationHeaders = false;
            _expanded.Clear();
        }
        else
        {
            foreach (var id in _expanded.Keys.Where(id => !snapshot.Conversation.Any(c => string.Equals(c.Id, id, StringComparison.Ordinal))).ToArray()) _expanded.Remove(id);
        }
        ListSync.SyncBy(Conversation, snapshot.Conversation, c => c.Id, EqualityComparer<ConversationItem>.Default.Equals);
        SyncConversationHeaders(snapshot.Conversation);
        ListSync.SyncBy(Banners, snapshot.Banners, b => b.Id, EqualityComparer<Reader.ReaderBanner>.Default.Equals);
        if (!string.Equals(_renamedEmailId, snapshot.EmailId, StringComparison.Ordinal))
        {
            // another message: its attachments have their own names (email-viewer.tsx:3101-3105)
            _renamed.Clear();
            _renamedEmailId = snapshot.EmailId;
        }

        ListSync.SyncBy(Attachments, Named(snapshot.Attachments), a => a.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), AttachmentCardsEqual);
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

    /// <summary>
    /// The name the user gave attachment <paramref name="index"/> while this message is open, or null when it keeps its own
    /// (Bulwark email-viewer.tsx:3101-3123: the rename is for saving a copy, the message is not changed, and another message shows the original names).
    /// </summary>
    public string? RenamedAttachment(int index) =>
        string.Equals(_renamedEmailId, _current.EmailId, StringComparison.Ordinal) && _renamed.TryGetValue(index, out var name) ? name : null;

    /// <summary>Renames one attachment of the open message: its tile and every save under it follow the new name until another message opens.</summary>
    public void RenameAttachment(int index, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        // index is the attachment's own Index (inline parts may be left out of the tiles, so it is not the tile position)
        var position = -1;
        for (var i = 0; i < Attachments.Count; i++)
        {
            if (Attachments[i].Index == index)
            {
                position = i;
                break;
            }
        }

        if (_current.BodyState == BodyState.Empty || position < 0)
        {
            return;
        }

        if (!string.Equals(_renamedEmailId, _current.EmailId, StringComparison.Ordinal))
        {
            _renamed.Clear();
            _renamedEmailId = _current.EmailId;
        }

        _renamed[index] = name;
        Attachments[position] = Attachments[position] with { Stem = Core.Mail.Attachments.AttachmentNames.Split(name).Stem };
    }

    // the cards of the snapshot with the names the user gave them in this reader
    private IReadOnlyList<AttachmentCard> Named(IReadOnlyList<AttachmentCard> cards) =>
        _renamed.Count == 0
            ? cards
            : [.. cards.Select(c => _renamed.TryGetValue(c.Index, out var name) ? c with { Stem = Core.Mail.Attachments.AttachmentNames.Split(name).Stem } : c)];
}
