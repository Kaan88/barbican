// Mirrors: Rust compose.rs (open :134, fields :169-339, draft :620-680, status :798); Bulwark components/email/email-composer.tsx (state :1300-1360, draftSender :3355, From select :5813, sub-address
// helper lib/sub-addressing.ts generateSubAddress, Cc/Bcc :6031-6056, Ctrl+Shift+C/B :5509); B-COMP-019..037; I-238, I-243, I-244, I-246, I-249, I-257, I-258.
// One open composer: the fields, the identity and From override, the recipient fields, the body (the editor view model, or a plain-text stand-in while no editor is registered), the attachments,
// the options and the status line. The leave guard, the autosave and the send flow are the other parts of this class (ComposerViewModel.*.cs). No UI types.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Editor;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Plugins.Payloads;
using Barbican.Presentation.Editor;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Compose;

/// <summary>The view model of one open composer.</summary>
public sealed partial class ComposerViewModel : ObservableObject, IMentionSource, IDisposable
{
    private static readonly Regex TagPattern = new("^[a-zA-Z0-9-]{1,30}$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly ComposerContext _c;
    private readonly ComposeSeed _seed;
    private readonly IReadOnlyList<ComposeIdentity> _identities;
    private readonly List<string> _ownDomains;
    private string _subject;
    private bool _showCc;
    private bool _showBcc;
    private string? _identityId;
    private bool _fromOverride;
    private string _fromName;
    private string _fromEmail;
    private string _tag = string.Empty;
    private bool _plain;
    private bool _readReceipt;
    private MessagePriority _priority;
    private string _fallbackBody;
    private string _status = string.Empty;
    private ComposerDialog? _dialog;
    private SuggestionSources _sources = new();

    public ComposerViewModel(ComposerContext context, ComposeSeed seed, IReadOnlyList<ComposeIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(identities);
        _c = context;
        _seed = seed;
        _identities = identities;
        _autosave = new AutosaveState(context.Time);
        Identities = [.. identities.Select(IdentityChoice.Of)];
        _ownDomains = [.. identities.Select(i => DomainOf(i.Identity.Email)).Where(d => d.Length > 0).Distinct(StringComparer.Ordinal)];
        _identityId = seed.IdentityId is { } id && Identities.Any(i => string.Equals(i.Id, id, StringComparison.Ordinal)) ? id : (Identities.Count > 0 ? Identities[0].Id : null);
        _fromOverride = seed.FromOverride is not null;
        _fromName = seed.FromOverride?.Name ?? string.Empty;
        _fromEmail = seed.FromOverride?.Email ?? string.Empty;
        _subject = seed.Subject;
        _plain = context.Settings.PlainTextMode;
        To = new RecipientFieldViewModel("To", () => _sources);
        Cc = new RecipientFieldViewModel("Cc", () => _sources);
        Bcc = new RecipientFieldViewModel("Bcc", () => _sources);
        To.Seed(seed.To);
        Cc.Seed(seed.Cc);
        Bcc.Seed(seed.Bcc);
        _showCc = seed.Cc.Trim().Length > 0;
        _showBcc = seed.Bcc.Trim().Length > 0;
        DraftId = seed.DraftId;
        Attachments = [.. seed.Attachments.Select(a => new AttachmentItem(a, AttachmentState.Done, null))];
        _fallbackBody = string.Empty;
        (Editor, _fallbackBody) = CreateBody(seed.Body, _plain);
        WireFields();
        _autosave.Start(ComputeKey(), seed.IsSaved);
        _lastNotified = ComputeKey();
        if (seed.IsSaved)
        {
            _status = "Draft saved";
        }
    }

    /// <summary>The id the plugin hooks name this composer by.</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    /// <summary>The account key the message is written from (the composer stays with its account when another becomes active).</summary>
    public string AccountKey => _seed.AccountId;

    public ComposeKind Kind => _seed.Kind;

    public string? SourceEmailId => _seed.SourceEmailId;

    /// <summary>The composer sits under the message it answers (<see cref="ComposerDock"/>) instead of taking the pane.</summary>
    public bool IsDocked { get; set; }

    public RecipientFieldViewModel To { get; }

    public RecipientFieldViewModel Cc { get; }

    public RecipientFieldViewModel Bcc { get; }

    public IReadOnlyList<IdentityChoice> Identities { get; }

    public ObservableCollection<AttachmentItem> Attachments { get; }

    /// <summary>The editor view model; null while no editor is registered (the stand-in text box then shows <see cref="FallbackBody"/>).</summary>
    public IEditorViewModel? Editor { get; private set; }

    /// <summary>The body text of the stand-in used when no editor view model could be created.</summary>
    public string FallbackBody
    {
        get => _fallbackBody;
        set => SetProperty(ref _fallbackBody, value ?? string.Empty);
    }

    /// <summary>The draft on the server (null before the first save).</summary>
    public string? DraftId { get; private set; }

    public string Subject
    {
        get => _subject;
        set => SetProperty(ref _subject, value ?? string.Empty);
    }

    public bool ShowCc
    {
        get => _showCc;
        set => SetProperty(ref _showCc, value);
    }

    public bool ShowBcc
    {
        get => _showBcc;
        set => SetProperty(ref _showBcc, value);
    }

    public string? SelectedIdentityId
    {
        get => _identityId;
        set
        {
            if (SetProperty(ref _identityId, value))
            {
                OnPropertyChanged(nameof(FromText));
                OnPropertyChanged(nameof(CanTag));
                OnPropertyChanged(nameof(SubAddressPreview));
                // onIdentitySelect (M6 set-data): another From identity was chosen in the composer
                if (value is not null && Identities.FirstOrDefault(i => string.Equals(i.Id, value, StringComparison.Ordinal)) is { } chosen)
                {
                    _c.Hooks.Fire(HookNames.OnIdentitySelect, () => new IdentityViewPayload(chosen.Id, chosen.Name, chosen.Email, null, null, string.Empty, string.Empty));
                }
            }
        }
    }

    /// <summary>"Edit sender": a From header typed instead of the identity's (I-244).</summary>
    public bool FromOverrideEnabled
    {
        get => _fromOverride;
        set
        {
            if (SetProperty(ref _fromOverride, value))
            {
                OnPropertyChanged(nameof(FromText));
            }
        }
    }

    public string FromOverrideName
    {
        get => _fromName;
        set => SetProperty(ref _fromName, value ?? string.Empty);
    }

    public string FromOverrideEmail
    {
        get => _fromEmail;
        set
        {
            if (SetProperty(ref _fromEmail, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(FromText));
            }
        }
    }

    /// <summary>The sub-address tag (<c>user+tag@domain</c>, tag 1 to 30 letters, digits and dashes); empty for none.</summary>
    public string SubAddressTag
    {
        get => _tag;
        set
        {
            if (SetProperty(ref _tag, (value ?? string.Empty).Trim()))
            {
                OnPropertyChanged(nameof(SubAddressError));
                OnPropertyChanged(nameof(SubAddressPreview));
                OnPropertyChanged(nameof(FromText));
            }
        }
    }

    /// <summary>The identity can take a tag: a plain address, a delimiter in the settings.</summary>
    public bool CanTag => CurrentIdentity is { } i && _c.Settings.SubAddressDelimiter.Length > 0 && i.Identity.Email.Contains('@', StringComparison.Ordinal) && !i.Identity.Email.StartsWith('*');

    /// <summary>Why the tag is not usable, or null.</summary>
    public string? SubAddressError => _tag.Length == 0 || TagPattern.IsMatch(_tag) ? null : "Use 1 to 30 letters, digits or dashes.";

    /// <summary>The address the tag makes, in Unicode (empty without a usable tag).</summary>
    public string SubAddressPreview => TaggedAddress() is { } a ? RecipientText.ToUnicodeEmail(a) : string.Empty;

    /// <summary>The From line as the footer shows it.</summary>
    public string FromText => EffectiveFrom() is { } f ? new IdentityChoice(string.Empty, f.Name, f.Email).Label : "No sending identity";

    public bool PlainText
    {
        get => _plain;
        set
        {
            if (_plain != value)
            {
                var doc = CurrentDoc();
                SetProperty(ref _plain, value);
                (Editor, _fallbackBody) = CreateBody(doc, value);
                OnPropertyChanged(nameof(Editor));
                OnPropertyChanged(nameof(FallbackBody));
            }
        }
    }

    public bool ReadReceipt
    {
        get => _readReceipt;
        set => SetProperty(ref _readReceipt, value);
    }

    public MessagePriority Priority
    {
        get => _priority;
        set => SetProperty(ref _priority, value);
    }

    /// <summary>"Saving…", "Draft saved 14:03", "Not saved" (I-238).</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>The question on screen, or null.</summary>
    public ComposerDialog? Dialog
    {
        get => _dialog;
        private set => SetProperty(ref _dialog, value);
    }

    /// <summary>The "To" fields' autocomplete sources arrived (Sent-based now, contacts in M5).</summary>
    public void SetSources(SuggestionSources sources) => _sources = sources ?? new SuggestionSources();

    /// <summary>The body as a document: the editor's, or the stand-in's text.</summary>
    public Doc CurrentDoc() => Editor?.Session.Doc ?? Doc.FromText(_fallbackBody);

    /// <summary>The ComposeDraft the pipeline saves or sends (the recipient fields as message text: groups expanded, IDN domains in ASCII).</summary>
    public ComposeDraft BuildDraft(bool commit = true)
    {
        if (commit)
        {
            CommitInputs();
        }

        return new ComposeDraft
        {
            AccountId = AccountKey,
            Kind = Kind,
            DraftId = DraftId,
            SourceEmailId = _seed.SourceEmailId,
            IdentityId = _identityId ?? string.Empty,
            FromOverride = FromOverrideValue(),
            To = RecipientText.ExpandForSend(To.Text),
            Cc = RecipientText.ExpandForSend(Cc.Text),
            Bcc = RecipientText.ExpandForSend(Bcc.Text),
            Subject = Subject,
            Body = CurrentDoc(),
            PlainText = _plain,
            Attachments = [.. Attachments.Where(a => a.State == AttachmentState.Done).Select(a => a.Attachment), .. _inline],
            InReplyTo = _seed.InReplyTo,
            References = _seed.References,
            ReadReceipt = _readReceipt,
            Priority = _priority,
        };
    }

    /// <summary>Puts what is typed in each recipient input into chips (before a send, a save, a close).</summary>
    public void CommitInputs()
    {
        To.Commit();
        Cc.Commit();
        Bcc.Commit();
    }

    /// <summary>Ctrl+Shift+C: shows the Cc field (I-249).</summary>
    public void RevealCc() => ShowCc = true;

    /// <summary>Ctrl+Shift+B: shows the Bcc field.</summary>
    public void RevealBcc() => ShowBcc = true;

    /// <summary>The composer's keys (Bulwark email-composer.tsx capture handler, spec/shortcuts.md): Ctrl+Enter sends, Ctrl+Shift+Enter opens the schedule dialog, Ctrl+S saves, Ctrl+Shift+C and B show Cc and Bcc.
    /// Escape belongs to the view (an open list or dialog first). True when the chord was ours.</summary>
    public bool HandleChord(string key, bool control, bool shift, bool alt)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!control || alt)
        {
            return false;
        }

        var k = key.ToLowerInvariant();
        switch (k, shift)
        {
            case ("enter", false):
                _ = SendAsync();
                return true;
            case ("enter", true):
                _ = OpenScheduleAsync();
                return true;
            case ("s", false):
                _ = SaveDraftAsync(close: false);
                return true;
            case ("c", true):
                RevealCc();
                return true;
            case ("b", true):
                RevealBcc();
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<MentionCandidate> Candidates(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var q = query.Trim().ToLowerInvariant();
        bool Hit(string name, string email) => q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase) || email.Contains(q, StringComparison.OrdinalIgnoreCase);
        var onField = RecipientText.Expand([.. To.Recipients, .. Cc.Recipients, .. Bcc.Recipients]).Where(r => Hit(r.Name, r.Email)).Select(r => new MentionCandidate(r.Name, r.Email));
        var rest = _sources.Contacts.Concat(_sources.Recent).Where(p => Hit(p.Name, p.Email)).Select(p => new MentionCandidate(p.Name, p.Email));
        return [.. onField.Concat(rest).DistinctBy(m => m.Email, StringComparer.OrdinalIgnoreCase).Take(10)];
    }

    /// <inheritdoc />
    public void Picked(MentionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var field = _c.MentionAddsRecipientTo switch
        {
            "cc" => Cc,
            "bcc" => Bcc,
            "to" => To,
            _ => null,
        };
        if (field is null || RecipientText.Expand([.. To.Recipients, .. Cc.Recipients, .. Bcc.Recipients]).Any(r => string.Equals(r.Email, candidate.Email, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (ReferenceEquals(field, Cc))
        {
            ShowCc = true;
        }
        else if (ReferenceEquals(field, Bcc))
        {
            ShowBcc = true;
        }

        _ = field.Add(Recipient.Addr(candidate.Name, candidate.Email));
    }

    public void Dispose()
    {
        _closed = true;
        foreach (var a in Attachments)
        {
            a.Cancel?.Cancel();
        }

        Dialog?.Complete(null);
    }

    private ComposeIdentity? CurrentIdentity => _identities.FirstOrDefault(i => string.Equals(i.Identity.Id, _identityId, StringComparison.Ordinal));

    private static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? string.Empty : email[(at + 1)..].Trim().ToLowerInvariant();
    }

    private string? TaggedAddress()
    {
        if (!CanTag || _tag.Length == 0 || !TagPattern.IsMatch(_tag))
        {
            return null;
        }

        var email = CurrentIdentity!.Identity.Email;
        var at = email.LastIndexOf('@');
        return string.Create(CultureInfo.InvariantCulture, $"{email[..at]}{_c.Settings.SubAddressDelimiter}{_tag}{email[at..]}");
    }

    /// <summary>Bulwark draftSender: an enabled override wins, else the identity with the sub-address tag.</summary>
    private FromOverride? EffectiveFrom()
    {
        if (_fromOverride && _fromEmail.Trim().Length > 0)
        {
            return new FromOverride(_fromName.Trim(), _fromEmail.Trim());
        }

        var identity = CurrentIdentity?.Identity;
        return identity is null ? null : new FromOverride(identity.Name ?? string.Empty, TaggedAddress() ?? identity.Email);
    }

    private FromOverride? FromOverrideValue()
    {
        if (_fromOverride && _fromEmail.Contains('@', StringComparison.Ordinal))
        {
            return new FromOverride(_fromName.Trim(), _fromEmail.Trim());
        }

        return TaggedAddress() is { } tagged ? new FromOverride(CurrentIdentity!.Identity.Name ?? string.Empty, tagged) : null;
    }

    private (IEditorViewModel? Editor, string Fallback) CreateBody(Doc doc, bool plain)
    {
        try
        {
            var vm = _c.Editors.Create(doc, plain);
            vm.Mentions = _c.MentionsEnabled ? this : null;
            return (vm, string.Empty);
        }
        catch (PortPendingException)
        {
            // no editor view model is registered (the editor stream has not landed): a plain text box stands in
            return (null, doc.TextRange(new Pos(0, 0), doc.End));
        }
    }

    private void WireFields()
    {
        foreach (var f in new[] { To, Cc, Bcc })
        {
            f.TextChanged += (_, _) => OnRecipientsChanged(f);
        }
    }
}
