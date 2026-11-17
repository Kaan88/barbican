// Mirrors: see IComposerService. Contract stub from the M3 plan; owner composer-ui replaces this class.

using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Presentation.Compose;

/// <summary>The default registration: nothing opens (IsOpen false), every call fails with <see cref="PortPendingException"/> until stream composer-ui lands.</summary>
public sealed class ComposerService : IComposerService
{
    public event EventHandler? OpenChanged
    {
        add { }
        remove { }
    }

    public bool IsOpen => false;

    public Task OpenAsync(ComposeRequest request, CancellationToken ct) => Task.FromException(new PortPendingException("ComposerService.OpenAsync"));

    public Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct) => Task.FromException(new PortPendingException("ComposerService.OpenSeedAsync"));

    public Task<bool> TryCloseAsync(CancellationToken ct) => Task.FromException<bool>(new PortPendingException("ComposerService.TryCloseAsync"));
}
