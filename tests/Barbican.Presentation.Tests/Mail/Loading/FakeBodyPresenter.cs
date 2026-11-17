// Test double for the body view: records every call; pages are ready at once (Auto) or when the test says so. Owner body.

using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

internal sealed class FakeBodyPresenter : IBodyPresenter
{
    public bool Auto { get; set; } = true;

    public List<string> Calls { get; } = [];

    public List<BodyRequest> Pending { get; } = [];

    public bool Dimmed { get; private set; }

    public bool Loading { get; private set; }

    public string? Shown { get; private set; }

    public BodyRequest? LastRequest { get; private set; }

    public void Show(BodyRequest request)
    {
        Calls.Add("show " + request.EmailId);
        LastRequest = request;
        Pending.Clear();
        Pending.Add(request);
        if (Auto)
        {
            Ready(request);
        }
    }

    /// <summary>The shell reports the page laid out: the loader's callback, then the swap.</summary>
    public bool Ready(BodyRequest request)
    {
        Pending.Remove(request);
        if (!request.OnReady())
        {
            Calls.Add("refused " + request.EmailId);
            return false;
        }

        Shown = request.EmailId;
        Dimmed = false;
        Calls.Add("reveal " + request.EmailId);
        request.OnShown?.Invoke();
        return true;
    }

    public void CancelPending()
    {
        Calls.Add("cancel");
        Pending.Clear();
    }

    public void SetDimmed(bool dimmed)
    {
        Calls.Add("dim " + dimmed);
        Dimmed = dimmed;
    }

    public void SetLoading(bool loading)
    {
        Calls.Add("loading " + loading);
        Loading = loading;
    }

    public void Clear()
    {
        Calls.Add("clear");
        Shown = null;
        Pending.Clear();
    }
}
