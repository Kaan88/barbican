// Mirrors: PORT-PLAN section 3.7 (a custom host: Scrim + PopIn, focus trapped and restored, Esc cancels, Enter accepts); Bulwark lib/keyboard.ts:355 (dialogs
// block app keys); Rust ctxmenu.rs PROMPT :32. One dialog at a time; the next waits. The in-window host (App) draws it. Owner menus.

using Barbican.Presentation.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Dialogs;

/// <summary>Shows dialogs one at a time and hands back their answers.</summary>
public sealed class DialogService : ObservableObject, IDialogService, IModalSurface, IDisposable
{
    private readonly SemaphoreSlim _one = new(1, 1);
    private ActiveDialog? _current;

    /// <summary>A dialog opened or closed (the host shows or hides the scrim).</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    public bool IsOpen => _current is not null;

    /// <summary>The dialog on screen, or null.</summary>
    public ActiveDialog? Current => _current;

    /// <inheritdoc />
    public async Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _one.WaitAsync(ct).ConfigureAwait(true);
        var done = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new ActiveDialog(request, request.Title, (request as PromptDialogRequest)?.Initial ?? string.Empty,
            answer => done.TrySetResult(answer));
        using var cancel = ct.Register(() => done.TrySetResult(null));
        try
        {
            Set(dialog);
            var answer = await done.Task.ConfigureAwait(true);
            return answer is TResult typed ? typed : default;
        }
        finally
        {
            Set(null);
            _ = _one.Release();
        }
    }

    public void Dispose() => _one.Dispose();

    /// <inheritdoc />
    public bool HandleKey(KeyChord chord)
    {
        var dialog = _current;
        if (dialog is null)
        {
            return false;
        }

        switch (chord.Key.ToLowerInvariant())
        {
            case "escape" or "esc":
                dialog.Complete(null);
                return true;
            case "enter" or "return":
                switch (dialog.Request)
                {
                    case ConfirmDialogRequest:
                        dialog.Complete(true);
                        break;
                    case PromptDialogRequest when dialog.Text.Trim().Length > 0:
                        dialog.Complete(dialog.Text.Trim());
                        break;
                    default:
                        // an owner's own dialog decides what Enter means
                        return false;
                }

                return true;
            default:
                // every other key belongs to the dialog's controls, and no app key runs behind it (I-168)
                return false;
        }
    }

    private void Set(ActiveDialog? dialog)
    {
        _current = dialog;
        OnPropertyChanged(nameof(IsOpen));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
