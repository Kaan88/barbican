// Mirrors: Bulwark components/email/email-composer.tsx close :5111 (Close and Esc ask), :6854-6879 (title, text and Cancel / Discard / Save of the close dialog; locales/en/common.json:1137-1138, 1048-1050), :6705 (Discard with a confirm); Rust compose.rs close and autosave.rs unedited
// (a blank composer closes and leaves no draft, L-008, L-009; a reply that holds only its quote and signature is no reason to keep one, B-COMP-006); I-234, I-235, I-236.
// The leave guard of ComposerViewModel.

using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Compose;

public sealed partial class ComposerViewModel
{
    /// <summary>The composer ended: the draft it left on the server (null when none) and whether the message was sent. The service drops it and fires <c>onComposerClose</c>.</summary>
    public event EventHandler<ComposerClosedEventArgs>? Closed;

    /// <summary>The composer has ended.</summary>
    public bool IsClosed => _closed;

    /// <summary>Close, Esc, or another composer replacing this one. False when the user chose Cancel (or a send is in flight).</summary>
    public async Task<bool> RequestCloseAsync()
    {
        if (_closed)
        {
            return true;
        }

        if (IsSending)
        {
            return false;
        }

        await WaitForSaveAsync().ConfigureAwait(true);
        CommitInputs();
        var key = ComputeKey();
        if (IsBlankNow())
        {
            await DiscardServerDraftAsync().ConfigureAwait(true);
            Finish(sent: false);
            return true;
        }

        if (_autosave.Unedited(key))
        {
            // the quote and the signature alone are no reason to keep a draft; a draft this composer made and the user then undid goes too
            if (DraftId is not null && !string.Equals(DraftId, _seed.DraftId, StringComparison.Ordinal))
            {
                await DiscardServerDraftAsync().ConfigureAwait(true);
            }

            Finish(sent: false);
            return true;
        }

        if (_autosave.IsSaved(key))
        {
            Finish(sent: false);
            return true;
        }

        return await AskToLeaveAsync(key).ConfigureAwait(true);
    }

    /// <summary>The trash button: asks, then removes the draft and closes (a blank composer closes without asking).</summary>
    public async Task<bool> DiscardAsync()
    {
        if (_closed)
        {
            return true;
        }

        if (!IsBlankNow())
        {
            var answer = await AskAsync(new ComposerDialog(
                "discard",
                "Discard draft?",
                "The draft will be deleted.",
                [new DialogChoice("discard", "Discard", DialogTone.Danger), new DialogChoice("cancel", "Cancel")])).ConfigureAwait(true);
            if (!string.Equals(answer, "discard", StringComparison.Ordinal))
            {
                return false;
            }
        }

        await WaitForSaveAsync().ConfigureAwait(true);
        await DiscardServerDraftAsync().ConfigureAwait(true);
        Finish(sent: false);
        return true;
    }

    /// <summary>Shows a question and waits for its answer (a choice id, or null for Escape). One question at a time: a second replaces the first with a cancel.</summary>
    public async Task<string?> AskAsync(ComposerDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (_c.AskInline is { } ask) return await ask(dialog).ConfigureAwait(true);
        Dialog?.Complete(null);
        Dialog = dialog;
        #pragma warning disable VSTHRD003 // the dialog is completed by its buttons
        var answer = await dialog.Answer.ConfigureAwait(true);
#pragma warning restore VSTHRD003
        if (ReferenceEquals(Dialog, dialog))
        {
            Dialog = null;
        }

        return answer;
    }

    /// <summary>The view calls this when a dialog button is pressed (null for Escape).</summary>
    public void AnswerDialog(string? choice) => Dialog?.Complete(choice);

    private async Task<bool> AskToLeaveAsync(ulong key)
    {
        var answer = await AskAsync(new ComposerDialog(
            "leave",
            "Save or discard draft?",
            "You have unsaved changes. Would you like to save this as a draft or discard it?",
            [new DialogChoice("save", "Save", DialogTone.Primary), new DialogChoice("discard", "Discard", DialogTone.Danger), new DialogChoice("cancel", "Cancel")])).ConfigureAwait(true);
        switch (answer)
        {
            case "save":
                if (!await SaveCoreAsync(key, explicitSave: true).ConfigureAwait(true))
                {
                    return false;
                }

                Finish(sent: false);
                return true;
            case "discard":
                await DiscardServerDraftAsync().ConfigureAwait(true);
                Finish(sent: false);
                return true;
            default:
                return false;
        }
    }

    private async Task DiscardServerDraftAsync()
    {
        if (DraftId is not { } id)
        {
            return;
        }

        try
        {
            var res = await _c.Drafts.DiscardAsync(AccountKey, id, CancellationToken.None).ConfigureAwait(true);
            if (!res.IsOk)
            {
                _ = _c.Toasts?.Show(new ToastRequest("Could not delete the draft: " + res.Error!.Message, IsError: true));
                return;
            }

            DraftId = null;
        }
#pragma warning disable CA1031 // a failed delete is a message; the composer still closes
        catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _ = _c.Toasts?.Show(new ToastRequest("Could not delete the draft: " + e.Message, IsError: true));
        }
    }

    private void Finish(bool sent)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        Dialog?.Complete(null);
        foreach (var a in Attachments)
        {
            a.Cancel?.Cancel();
        }

        Closed?.Invoke(this, new ComposerClosedEventArgs(DraftId, sent));
    }
}
