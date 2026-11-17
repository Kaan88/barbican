// Mirrors: Bulwark components/email/email-composer.tsx close dialog :6916-6923, send-warnings-dialog.tsx, external-recipient-confirm.tsx; I-235, I-259, I-260. The composer's own question
// (its buttons are the answer), shown inside the composer pane so it works for a docked reply too. Owner composer-ui.

namespace Barbican.Presentation.Compose;

/// <summary>A question the composer asks: a title, a message, optional bullet lines and the buttons that answer it. <see cref="Answer"/> completes with the chosen id, or null when cancelled (Escape).</summary>
public sealed class ComposerDialog
{
    private readonly TaskCompletionSource<string?> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ComposerDialog(string id, string title, string message, IReadOnlyList<DialogChoice> choices, IReadOnlyList<string>? lines = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(choices);
        Id = id;
        Title = title;
        Message = message;
        Choices = choices;
        Lines = lines ?? [];
    }

    /// <summary>A stable name for automation and tests ("leave", "warnings", "discard", "pending-uploads").</summary>
    public string Id { get; }

    public string Title { get; }

    public string Message { get; }

    /// <summary>Bullet lines under the message (the warnings, the external addresses, the placeholders).</summary>
    public IReadOnlyList<string> Lines { get; }

    public IReadOnlyList<DialogChoice> Choices { get; }

    /// <summary>The answer: a choice id, or null.</summary>
#pragma warning disable VSTHRD003 // the source is completed by the dialog's own buttons and never by outside work
    public Task<string?> Answer => _answer.Task;
#pragma warning restore VSTHRD003

    /// <summary>Ends the dialog (a button, or null for Escape).</summary>
    public void Complete(string? choice) => _answer.TrySetResult(choice);
}
