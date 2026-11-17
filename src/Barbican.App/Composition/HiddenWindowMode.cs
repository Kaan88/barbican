namespace Barbican.App.Composition;

/// <summary>How the main window comes up; decided once at start (<see cref="HiddenWindowOptions.Decide"/>).</summary>
public enum HiddenWindowMode
{
    /// <summary>A normal, activated window: only a user's own start of a Debug or Release build.</summary>
    Normal,

    /// <summary>
    /// Windows: off-screen and non-activating from the first frame (<c>HiddenWindow.Show</c>,
    /// docs/adr/0007). Every AUTOMATION build and every <c>--smoke</c> run on Windows.
    /// </summary>
    OffScreen,

    /// <summary>
    /// Linux: activated, but only on the VM's private Xvfb display (tools/linux.ps1 xrun announces it
    /// with <c>BARBICAN_XVFB=1</c>). Every AUTOMATION build and every <c>--smoke</c> run on Linux.
    /// </summary>
    PrivateDisplay,
}
