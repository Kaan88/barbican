// Mirrors: Bulwark lib/plugin-sandbox/open-email.ts (OpenEmailHandler, onOpenEmail), calendar-navigation.ts (requestCalendarView), open-task.ts (OpenTaskHandler, onOpenTask).
// Host seam: what the shell does when an add-on asks to move the user's view. The Core methods have already checked the grant, the login and the rate; an implementation only moves the UI, on the UI thread, and
// answers false/Refused when it cannot (it never throws for a refusal). It never reaches a screen other than the mail reader, the calendar's own views and its task editor.
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Plugins.Api.Navigation;

/// <summary>Moves the app's view for an add-on.</summary>
public interface IAppNavigator
{
    /// <summary>Opens <paramref name="summary"/> in the reader. <paramref name="accountKey"/> is the login the add-on runs on; an implementation refuses when that login is no longer the one on screen.</summary>
    Task<PluginOpenEmailOutcome> OpenEmailAsync(string accountKey, string? sharedAccountId, EmailSummary summary, CancellationToken ct);

    /// <summary>Moves to a calendar view; false when nothing could take it (the Tasks view while tasks are off, the login on screen changed).</summary>
    Task<bool> OpenCalendarAsync(string accountKey, PluginCalendarView view, CancellationToken ct);

    /// <summary>Opens the task in its editor while the calendar is on screen and has the task; false otherwise (Bulwark open-task.ts:45-50).</summary>
    Task<bool> OpenTaskAsync(string accountKey, string taskId, CancellationToken ct);
}
