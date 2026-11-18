using Barbican.Core.Calendar.Editing;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Compose;
using CommunityToolkit.Mvvm.ComponentModel;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The state of the calendar's card layer.</summary>
public sealed class CalendarEditorHost : ObservableObject, ICalendarQuestions, Commands.ICalendarKeyContext
{
    private object? _card;
    private ComposerDialog? _question;
    private bool _notify = true;

    /// <summary>The card on the scrim: <see cref="EventEditorViewModel"/>, <see cref="TaskEditorViewModel"/>, <see cref="QuickCreateViewModel"/>, a dialog view model or the tasks preview; null when none.</summary>
    public object? Card
    {
        get => _card;
        private set
        {
            if (SetProperty(ref _card, value))
            {
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(EditorOpen));
            }
        }
    }

    /// <summary>The question above the card (scope, discard, delete), or null.</summary>
    public ComposerDialog? Question
    {
        get => _question;
        private set
        {
            if (SetProperty(ref _question, value))
            {
                OnPropertyChanged(nameof(IsOpen));
                OnPropertyChanged(nameof(EditorOpen));
            }
        }
    }

    /// <summary>"Notify guests" on the scope question of a delete.</summary>
    public bool Notify
    {
        get => _notify;
        set => SetProperty(ref _notify, value);
    }

    public bool IsOpen => _card is not null || _question is not null;

    /// <summary>A card or a question is on screen: the calendar's single keys are off, Ctrl+Enter saves and Esc closes (<c>ShortcutContext.EditorOpen</c>).</summary>
    public bool EditorOpen => _card is not null || _question is not null;

    public void Show(object card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
    }

    /// <summary>Closes <paramref name="card"/> when it is the one shown (a late close of an old card does nothing).</summary>
    public void Close(object card)
    {
        if (ReferenceEquals(_card, card))
        {
            Card = null;
        }
    }

    /// <summary>Escape: the question first (answered null), else the card asks to close itself.</summary>
    public void Escape()
    {
        if (_question is { } q)
        {
            q.Complete(null);
            return;
        }

        switch (_card)
        {
            case EventEditorViewModel e:
                _ = e.RequestCloseAsync(CancellationToken.None);
                break;
            case TaskEditorViewModel t:
                t.Close();
                break;
            case ICloseableCard c:
                c.Close();
                break;
            default:
                Card = null;
                break;
        }
    }

    public async Task<string?> AskAsync(ComposerDialog dialog, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        Question = dialog;
        try
        {
            using var _ = ct.Register(() => dialog.Complete(null));
#pragma warning disable VSTHRD003 // the source is completed by the dialog's own buttons
            return await dialog.Answer.ConfigureAwait(true);
#pragma warning restore VSTHRD003
        }
        finally
        {
            if (ReferenceEquals(Question, dialog))
            {
                Question = null;
            }
        }
    }

    public async Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(title);
        var verb = forDelete ? "Delete" : "Edit";
        var dialog = new ComposerDialog(
            "scope",
            forDelete ? "Delete recurring event" : "Edit recurring event",
            $"{verb} \"{title}\": which events?",
            [
                new DialogChoice("occurrence", "This event"),
                new DialogChoice("following", "This and following events"),
                new DialogChoice("all", "All events", forDelete ? DialogTone.Danger : DialogTone.Primary),
                new DialogChoice("cancel", "Cancel"),
            ],
            forDelete && hasGuests ? ["Guests will be notified of the cancellation."] : null);
        Notify = hasGuests;
        var answer = await AskAsync(dialog, ct).ConfigureAwait(true);
        var scope = answer switch
        {
            "occurrence" => (EditScope?)EditScope.Occurrence,
            "following" => EditScope.Following,
            "all" => EditScope.All,
            _ => null,
        };
        return scope is { } s ? (s, forDelete && hasGuests && Notify) : null;
    }

    /// <summary>The yes/no question of a delete, discard or similar; true for the first (confirming) choice.</summary>
    public async Task<bool> ConfirmAsync(string id, string title, string message, string confirmLabel, bool danger, CancellationToken ct)
    {
        var dialog = new ComposerDialog(id, title, message, [new DialogChoice("yes", confirmLabel, danger ? DialogTone.Danger : DialogTone.Primary), new DialogChoice("no", "Cancel")]);
        return string.Equals(await AskAsync(dialog, ct).ConfigureAwait(true), "yes", StringComparison.Ordinal);
    }
}
