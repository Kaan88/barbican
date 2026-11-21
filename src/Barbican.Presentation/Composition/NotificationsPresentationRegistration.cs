// Mirrors: M6b feature parity G-059, G-045, G-041 (DI of the notification services). Owner notifications.

using Barbican.Core.Notifications;
using Barbican.Presentation.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barbican.Presentation.Composition;

/// <summary>The new-mail notification service, the notification centre and the bell.</summary>
public static class NotificationsPresentationRegistration
{
    public static IServiceCollection AddBarbicanNotifications(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDesktopNotifier, NullDesktopNotifier>();
        services.TryAddSingleton<NotificationCenter>();
        services.TryAddSingleton<INotificationFolderInfo, NotificationFolderInfo>();
        services.TryAddSingleton<NewMailNotificationService>();
        services.AddSingleton<IStartupTask>(sp => sp.GetRequiredService<NewMailNotificationService>());
        services.TryAddSingleton<NotificationCenterViewModel>();
        return services;
    }
}
