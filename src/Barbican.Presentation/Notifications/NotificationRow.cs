// Mirrors: Bulwark components/notifications/notification-center.tsx:86-108 (one entry). Owner notifications.

namespace Barbican.Presentation.Notifications;

/// <summary>One entry of the panel as it is shown.</summary>
public sealed record NotificationRow(string Id, string Title, string Body, string Source, string When, bool Unread, string Level);
