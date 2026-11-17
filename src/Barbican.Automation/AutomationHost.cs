using Barbican.Automation.Protocol;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.Automation;

/// <summary>
/// Starts the hidden automation of an AUTOMATION build (docs/adr/0007): the popup guard and the
/// loopback line server. Called once by the App after the window was placed off-screen (Windows) or
/// shown on the private Xvfb (Linux). Without a usable BARBICAN_AUTOMATION_TOKEN the port is never opened (Fable review 1 F-08).
/// </summary>
public static class AutomationHost
{
    private static AutomationServer? _server;
    private static bool _started;

    public static void Start(Window window, IServiceProvider services, int port)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(services);
        if (_started)
        {
            return;
        }

        _started = true;

        // the popup guard runs whether or not the port opens: a hidden window must never show a popup on the user's screen
        var context = new AutomationContext(window);
        var guard = new PopupGuard(context);
        guard.Start();
        if (AutomationConnection.TokenFromEnvironment() is not { } token)
        {
            Console.WriteLine($"[automation] no {AutomationConnection.TokenVariable} (at least {AutomationConnection.MinTokenLength} characters): the port stays closed");
            return;
        }

        var commands = services.GetServices<IAutomationCommand>().ToArray();
        _server = new AutomationServer(context, new CommandRouter(context, guard, commands), port, token);
        _server.Start();
        Console.WriteLine($"[automation] listening on 127.0.0.1:{port}");
    }
}
