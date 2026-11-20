using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Allows one trusted parent and one host-selected child navigation per WebView.</summary>
internal sealed class PluginSandboxNavigationGate(PluginSandboxResourcePolicy policy, PluginFrameKind kind)
{
    private readonly string _frameUrl = kind switch
    {
        PluginFrameKind.Background => "https://plugin.invalid/sandbox/index.html",
        PluginFrameKind.Slot => "https://plugin.invalid/sandbox/index.html?frame=slot",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
    private bool _parentStarted;
    private bool _frameStarted;
    private bool _rejected;

    public bool TryStart(string url, bool topLevel)
    {
        if (_rejected) return false;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (topLevel ? CanStartParent(uri) : CanStartFrame(uri)))
        {
            if (topLevel) _parentStarted = true;
            else _frameStarted = true;
            return true;
        }

        _rejected = true;
        return false;
    }

    private bool CanStartParent(Uri uri) =>
        !_parentStarted && policy.Allows(uri, "top-navigation");

    private bool CanStartFrame(Uri uri) =>
        _parentStarted && !_frameStarted && string.Equals(uri.OriginalString, _frameUrl, StringComparison.Ordinal) &&
        policy.Allows(uri, "frame-navigation");
}
