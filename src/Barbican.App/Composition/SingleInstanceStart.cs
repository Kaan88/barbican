// Mirrors: Rust main.rs (instance::forward before the window: when the OS starts Barbican for a mailto: link while one runs, the new process hands the link over and exits); PORT-PLAN section 3.9. Owner composer-ui.

using Barbican.Core.Instance;
using Barbican.Core.Storage;

namespace Barbican.App.Composition;

/// <summary>The launch-time half of the single-instance hand-off.</summary>
public static class SingleInstanceStart
{
    /// <summary>True when an existing instance of this profile took a compose link from the command line.</summary>
    public static bool ForwardLink(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (InstanceHandoff.ComposeLinkArg(args) is not { } url)
        {
            return false;
        }

        using var instance = new SingleInstance(ProfilePaths.FromEnvironment().ConfigDir);
        return instance.Forward(url);
    }
}
