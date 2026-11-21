// Mirrors: Bulwark stores/notification-center-store.ts:13-27 CenterEntry. Owner notifications.

namespace Barbican.Core.Notifications;

/// <summary>One line of the notification centre.</summary>
/// <param name="Id">Unique id.</param>
/// <param name="Time">When it came.</param>
/// <param name="Level">error, warning or info.</param>
/// <param name="Title">The headline.</param>
/// <param name="Body">The text under it.</param>
/// <param name="Source">Whose it is: the app, a plugin's name, or the kind (mail, share, calendar).</param>
/// <param name="Read">Whether it was seen.</param>
/// <param name="AccountKey">For mail: the account; a click opens the message.</param>
/// <param name="EmailId">For mail: the message.</param>
public sealed record NotificationEntry(string Id, DateTimeOffset Time, string Level, string Title, string Body, string Source, bool Read, string? AccountKey = null, string? EmailId = null);
