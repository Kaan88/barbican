// Mirrors: Rust palette.rs:550 open_hit (a mail hit: go to Mail, show its folder, open the message once the folder's list has it); Bulwark lib/global-search/open-hit.ts (a contact opens in Contacts, an event in the Calendar on its
// day, a file in Files in its folder with the file selected). T-102 (the folder of a hit).

using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Contacts;
using Barbican.Presentation.Files;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Search;

/// <summary>Opens a search hit where it lives: the app that holds it, on the place it is in.</summary>
public sealed class SearchHitOpener(
    MessageListViewModel list,
    SidebarViewModel sidebar,
    ShellViewModel? shell = null,
    IContactsViewModel? contacts = null,
    ICalendarViewModel? calendar = null,
    IFilesViewModel? files = null)
{
    /// <summary>Shows the hit's folder (unless it is the open one) and selects the message once the list has it; false when the message is not among the rows loaded (beyond the first page).</summary>
    public async Task<bool> OpenMailAsync(string emailId, string folderId)
    {
        ArgumentException.ThrowIfNullOrEmpty(emailId);
        _ = shell?.TryActivate(AppId.Mail);
        if (folderId.Length > 0 && !string.Equals(list.MailboxId, folderId, StringComparison.Ordinal))
        {
            await sidebar.SelectFolderAsync(folderId).ConfigureAwait(true);
        }

        var row = list.Messages.FirstOrDefault(m => string.Equals(m.Id, emailId, StringComparison.Ordinal));
        if (row is null)
        {
            return false;
        }

        list.Select(row, OpenReason.Search, ListDirection.None);
        return true;
    }

    /// <summary>Goes to Contacts and opens the card in the detail pane; false without the Contacts app.</summary>
    public bool OpenContact(string cardId)
    {
        ArgumentException.ThrowIfNullOrEmpty(cardId);
        if (contacts is null || shell is null || !shell.TryActivate(AppId.Contacts))
        {
            return false;
        }

        contacts.Open(cardId);
        return true;
    }

    /// <summary>Goes to the Calendar, shows the day and selects the event; false without the Calendar app.</summary>
    public bool OpenEvent(string eventId, DateOnly day)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventId);
        if (calendar is null || shell is null || !shell.TryActivate(AppId.Calendar))
        {
            return false;
        }

        calendar.ShowDay(day);
        calendar.SelectOccurrence(eventId);
        return true;
    }

    /// <summary>Goes to Files, shows the folder the entry is in (a folder opens itself) and selects it; false without the Files app.</summary>
    public bool OpenFile(string key, string folderKey, bool isFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (files is null || shell is null || !shell.TryActivate(AppId.Files))
        {
            return false;
        }

        if (isFolder)
        {
            files.Navigate(key);
            return true;
        }

        files.Navigate(folderKey.Length > 0 ? folderKey : null);
        files.SelectFile(key, false);
        return true;
    }
}
