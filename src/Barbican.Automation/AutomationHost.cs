using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Automation;

/// <summary>
/// Starts the hidden automation of an AUTOMATION build (docs/adr/0007): the popup guard and the
/// loopback line server. Called once by the App after the window was placed off-screen (Windows) or
/// shown on the private Xvfb (Linux).
/// </summary>
public static class AutomationHost
{
    private static AutomationServer? _server;

    public static void Start(Window window, IServiceProvider services, int port)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(services);
        if (_server is not null)
        {
            return;
        }

        var context = new AutomationContext(window);
        var guard = new PopupGuard(context);
        guard.Start();
        var commands = services.GetServices<IAutomationCommand>().ToArray();
        _server = new AutomationServer(context, new CommandRouter(context, guard, commands), port);
        _server.Start();
        Console.WriteLine($"[automation] listening on 127.0.0.1:{port}");
    }
}
