// Mirrors: M2 plan section 4.2 (IDialogService); PORT-PLAN section 3.7 (Scrim + PopIn, focus trapped and restored, Esc and Enter, in-window). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Dialogs;

/// <summary>Shows one dialog at a time and returns its answer (null when cancelled).</summary>
public interface IDialogService
{
    Task<TResult?> ShowAsync<TResult>(DialogRequest<TResult> request, CancellationToken ct);

    bool IsOpen { get; }
}
