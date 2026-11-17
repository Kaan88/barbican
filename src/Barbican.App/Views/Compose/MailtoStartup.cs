// Mirrors: Rust instance.rs listen :124 / cleanup :145 and main.rs start-up (a link on the command line opens the composer; later launches hand their link to this instance); PORT-PLAN section 3.9; plan D-241 (loopback TCP with a token).
// Start-up of the first instance: listens for hand-offs (the endpoint file lives in the profile's config folder, so an automation profile never takes the user's links), routes a link given on its own command line, and removes the file
// when the process ends. A launch that has a link and finds an instance running forwards it before any window exists (SingleInstanceStart). Owner composer-ui.

using Barbican.Core.Instance;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Composition;

namespace Barbican.App.Views.Compose;

/// <summary>Listens for mailto hand-offs and routes the first link.</summary>
public sealed class MailtoStartup(IProfilePaths paths, MailtoRouter router, IDispatcher dispatcher) : IStartupTask, IDisposable
{
    private SingleInstance? _instance;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _instance = new SingleInstance(paths.ConfigDir);
        if (_instance.Listen(url => dispatcher.Post(() => _ = router.HandleAsync(url, CancellationToken.None))))
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Dispose();
        }

        if (InstanceHandoff.MailtoArg(Environment.GetCommandLineArgs().Skip(1).ToArray()) is { } first)
        {
            _ = router.HandleAsync(first, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _instance?.Dispose();
}
