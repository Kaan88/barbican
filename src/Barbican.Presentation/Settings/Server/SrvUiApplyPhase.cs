// Mirrors: see SrvUiFilterApplyModel. Owner srv-ui.

namespace Barbican.Presentation.Settings.Server;

/// <summary>Where the apply dialog is.</summary>
public enum SrvUiApplyPhase
{
    Scanning,
    Ready,
    Nothing,
    Unsupported,
    Applying,
    Done,
    Failed,
}
