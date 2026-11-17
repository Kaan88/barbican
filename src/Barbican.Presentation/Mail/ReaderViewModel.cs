// Mirrors: M1-plan §4.3 (Header, Conversation strip, Attachments, AuthResults, BodyState, Apply(ReaderSnapshot)); Rust msgload.rs (header and body
// change together, B-SYNC-022; a failed fetch leaves nothing of the previous message, B-SYNC-029); spec I-194..I-233; M2 banners (reader). Owner reader (M2).

using CommunityToolkit.Mvvm.ComponentModel;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Mail;

/// <summary>The reader. <see cref="Apply"/> changes everything in one call, so the header and the body of one message never show apart.</summary>
public sealed class ReaderViewModel : ObservableObject
{
    private ReaderSnapshot _current = ReaderSnapshot.Empty;

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

    /// <summary>The attachment tiles; synced in place so a thumbnail arriving touches one tile.</summary>
    public BulkObservableCollection<AttachmentCard> Attachments { get; } = [];

    /// <summary>The banners above the body; synced in place with the snapshot (same turn as the reveal).</summary>
    public BulkObservableCollection<Reader.ReaderBanner> Banners { get; } = [];

    /// <summary>Takes over a prepared snapshot: every property and both collections change here, then the notifications go out together.</summary>
    public void Apply(ReaderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var previous = _current;
        _current = snapshot;
        ListSync.SyncBy(Conversation, snapshot.Conversation, c => c.Id, EqualityComparer<ConversationItem>.Default.Equals);
        ListSync.SyncBy(Banners, snapshot.Banners, b => b.Id, EqualityComparer<Reader.ReaderBanner>.Default.Equals);
        ListSync.SyncBy(Attachments, snapshot.Attachments, a => a.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), AttachmentCardsEqual);
        OnPropertyChanged(nameof(Current));
        if (!string.Equals(previous.EmailId, snapshot.EmailId, StringComparison.Ordinal))
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
