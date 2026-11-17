// Mirrors: Rust render_current_then :142 for the native renderers ("the native renderers show the body at once"; tag rust-final). Owner body.

namespace Barbican.Presentation.Mail.Loading;

/// <summary>A body view that shows nothing and is ready at once (tests, runs without a window).</summary>
public sealed class NullBodyPresenter : IBodyPresenter
{
    public void Show(BodyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OnReady())
        {
            request.OnShown?.Invoke();
        }
    }

    public void CancelPending()
    {
    }

    public void SetDimmed(bool dimmed)
    {
    }

    public void SetLoading(bool loading)
    {
    }

    public void Clear()
    {
    }
}
