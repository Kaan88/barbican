// Mirrors: M1-plan §3.3 (views get their ViewModels from DI, never with new in code-behind). BarbicanApp (App.xaml.cs, F1) builds the host and creates
// ShellPage with new, and does not expose the service provider, so the provider is captured when the container is built (see CaptureProvider).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Dispatching;

namespace Barbican.App.Shell;

/// <summary>Where views find their services. Set once, on the UI thread, while <c>AppComposition.Build</c> runs.</summary>
public static class ViewHost
{
    private static IServiceProvider? _services;

    /// <summary>The UI thread's queue, taken at the same moment (Build runs on the UI thread in OnLaunched).</summary>
    public static DispatcherQueue? UiQueue { get; private set; }

    /// <summary>The application's service provider.</summary>
    public static IServiceProvider Services => _services ?? throw new InvalidOperationException("The view host has no service provider yet: AddBarbicanViews must be part of the composition root, and the host must be built before the first view.");

    /// <summary>A service, or the failure that tells which registration is missing.</summary>
    public static T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>A service that may be absent (a feature that another stream completes).</summary>
    public static T? Find<T>()
        where T : class => Services.GetService<T>();

    /// <summary>
    /// Hooked into the container as an <see cref="ILoggerProvider"/>: Host.Build resolves the logger factory (hence every provider), which hands over the
    /// finished provider without anyone having to edit the composition root or App.xaml.cs. The provider logs nothing.
    /// </summary>
    internal static ILoggerProvider CaptureProvider(IServiceProvider services)
    {
        _services = services;
        UiQueue = DispatcherQueue.GetForCurrentThread();
        return NullLoggerProvider.Instance;
    }
}
