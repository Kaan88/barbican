// Mirrors: Rust mailview.rs (tag rust-final) show :158, cancel_pending :190, dim :199, clear :240, and msgload.rs r_loading (the thin
// loading bar). PORT-PLAN.md §3.6 (one persistent shell, frames swapped in once laid out). Implemented by the App's MailBodyView host;
// NullBodyPresenter stands in where no view exists (tests, headless runs). Owner body.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The body view as the message loader drives it. Every member is called on the UI thread.</summary>
public interface IBodyPresenter
{
    /// <summary>
    /// Loads a message hidden next to the one on screen. <see cref="BodyRequest.OnReady"/> runs on the UI thread right before the swap
    /// (false drops the page); <see cref="BodyRequest.OnShown"/> after it.
    /// </summary>
    void Show(BodyRequest request);

    /// <summary>Drops the page that is loading, if any (another message is being opened); what is on screen stays.</summary>
    void CancelPending();

    /// <summary>Fades the page on screen while the next loads (it stays until the next is ready).</summary>
    void SetDimmed(bool dimmed);

    /// <summary>The thin loading bar above the body.</summary>
    void SetLoading(bool loading);

    /// <summary>Empties the view (the reader closed or the message failed), in the reader's colour.</summary>
    void Clear();
}
