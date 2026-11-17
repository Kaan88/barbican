using Barbican.App.MailBody;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap;
using Barbican.Presentation.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#if AUTOMATION
using Barbican.Automation;
#endif

namespace Barbican.App.Composition;

/// <summary>
/// The composition root (docs/milestones/M1-plan.md §3.3), frozen after stage F1: it builds an empty
/// Microsoft.Extensions.Hosting host (no configuration files, no file watchers, nothing on the
/// cold-start path that the App does not use) and calls each feature's registration exactly once,
/// in this order, so a later feature's <c>Replace</c> wins over an earlier <c>TryAdd</c>. No stream
/// edits this file; each owns its own Add method.
/// </summary>
public static class AppComposition
{
    public static IHost Build(HiddenWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var services = builder.Services;
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(options);

        services.AddBarbicanCore();         // jmap:     src/Barbican.Core/Jmap/JmapRegistration.cs
        services.AddBarbicanMailCore();     // mailcore: src/Barbican.Presentation/Composition/MailPresentationRegistration.cs
        services.AddBarbicanAccounts();     // accounts: src/Barbican.Core/Accounts/AccountsRegistration.cs
        services.AddBarbicanPlatform();     // accounts: src/Barbican.App/Composition/PlatformServices.cs
        services.AddBarbicanViews();        // shell-ui: src/Barbican.App/Composition/ViewServices.cs
        services.AddBarbicanMailBody();     // body:     src/Barbican.App/MailBody/MailBodyRegistration.cs
#if AUTOMATION
        services.AddBarbicanAutomation();   // F1:       src/Barbican.Automation/AutomationRegistration.cs
#endif
        return builder.Build();
    }
}
