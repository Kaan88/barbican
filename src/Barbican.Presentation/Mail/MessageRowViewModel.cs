// Mirrors: Rust mail.rs build_rows :463 (Mail struct of ui/app.slint); Bulwark components/email/thread-list-item.tsx (sender, subject, preview,
// date, unread dot, star, attachment chips, thread count, tag dots); spec I-119..I-129. Owner mailcore.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Rules;

namespace Barbican.Presentation.Mail;

/// <summary>One message row, immutable: a change in the data makes a new row that ListSync sets in place.</summary>
public sealed class MessageRowViewModel : ListItemViewModel
{
    private bool _isChecked;

    public MessageRowViewModel(EmailSummary summary)
        : this(summary, new RowContext())
    {
    }

    public MessageRowViewModel(EmailSummary summary, RowContext context)
        : this(summary, context, [])
    {
    }

    public MessageRowViewModel(EmailSummary summary, RowContext context, IReadOnlyList<string> badges)
    {
        ArgumentNullException.ThrowIfNull(badges);
        Badges = badges;
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(context);
        Summary = summary;
        Sender = RowText.Sender(summary);
        Initials = RowText.Initials(Sender);
        TintIndex = RowText.TintIndex(Sender);
        Subject = string.IsNullOrWhiteSpace(summary.Subject) ? "(no subject)" : summary.Subject;
        Preview = context.ShowPreview ? RowText.CleanPreview(summary.Preview) : string.Empty;
        TimeLabel = summary.ReceivedAt is { } at ? RowText.TimeLabel(at, context.Now, context.Zone, context.DateFormat, context.TwelveHour) : string.Empty;
        Unread = !summary.Keywords.Seen;
        Flagged = summary.Keywords.Flagged;
        ThreadCount = Math.Max(summary.ThreadCount, 1);
        TagColors = [.. summary.Keywords.Map
            .Where(k => k.Value && k.Key.StartsWith("$label:", StringComparison.Ordinal))
            .Select(k => context.TagColor(k.Key["$label:".Length..]))];
        AttachmentNames = context.AttachmentChips ? RowText.AttachmentNames(summary, context.HideInlineImages) : [];
        HasAttachment = summary.HasAttachment;
    }

    public EmailSummary Summary { get; }

    /// <summary>The row's tick (multi-select, I-120): the list sets it from its own checked set after every rebuild.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    public override string Key => Summary.Id;

    public string Id => Summary.Id;

    public string Sender { get; }

    public string Initials { get; }

    /// <summary>0..3: which avatar tint the sender hashes to.</summary>
    public int TintIndex { get; }

    public string Subject { get; }

    public string Preview { get; }

    public string TimeLabel { get; }

    public bool Unread { get; }

    public bool Flagged { get; }

    /// <summary>Messages in the conversation; the row shows a count from 2.</summary>
    public int ThreadCount { get; }

    /// <summary>The 0xRRGGBB of each tag the message carries.</summary>
    public IReadOnlyList<uint> TagColors { get; }

    public IReadOnlyList<string> AttachmentNames { get; }

    public bool HasAttachment { get; }

    /// <summary>Labels plugins add next to the subject (hook onEmailListItemRender); empty without plugins.</summary>
    public IReadOnlyList<string> Badges { get; }

    public override bool SameAs(ListItemViewModel other) =>
        other is MessageRowViewModel r
        && string.Equals(r.Summary.Id, Summary.Id, StringComparison.Ordinal)
        && string.Equals(r.Sender, Sender, StringComparison.Ordinal)
        && string.Equals(r.Subject, Subject, StringComparison.Ordinal)
        && string.Equals(r.Preview, Preview, StringComparison.Ordinal)
        && string.Equals(r.TimeLabel, TimeLabel, StringComparison.Ordinal)
        && r.Unread == Unread
        && r.Flagged == Flagged
        && r.ThreadCount == ThreadCount
        && r.HasAttachment == HasAttachment
        && r.TagColors.SequenceEqual(TagColors)
        && r.Badges.SequenceEqual(Badges, StringComparer.Ordinal)
        && r.AttachmentNames.SequenceEqual(AttachmentNames, StringComparer.Ordinal);
}
