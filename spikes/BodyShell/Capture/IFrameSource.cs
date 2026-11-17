namespace Barbican.Spike.BodyShell.Capture;

/// <summary>A capture of the spike window: every composed frame (Windows.Graphics.Capture) or polled snapshots (X11).</summary>
internal interface IFrameSource : IDisposable
{
    string Name { get; }

    void Start(Action<CapturedFrame> sink);

    Task StopAsync();

    /// <summary>One frame now (used by the Linux stand-in for the overlay strategy).</summary>
    CapturedFrame? Grab();
}
