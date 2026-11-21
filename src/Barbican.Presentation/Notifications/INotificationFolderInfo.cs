// Mirrors: Bulwark lib/desktop/new-mail.ts:44-47 (the folders of the account by role, and its notification mutes: stores/settings-store.ts notificationMutes, lib/notification-mutes.ts). Owner notifications.

namespace Barbican.Presentation.Notifications;

/// <summary>What the new-mail rules need to know about an account's folders.</summary>
public interface INotificationFolderInfo
{
    /// <summary>The role to folder id map (inbox, junk, trash, sent, drafts, archive) and the ids of the muted folders.</summary>
    Task<(IReadOnlyDictionary<string, string> RoleIds, IReadOnlySet<string> Muted)> GetAsync(string accountKey, CancellationToken ct);

    /// <summary>The cached mutes changed (a folder was muted or unmuted): ask again.</summary>
    void Invalidate(string? accountKey = null);
}
