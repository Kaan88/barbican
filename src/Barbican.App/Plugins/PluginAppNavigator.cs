#if WINDOWS
// Mirrors: Bulwark lib/plugin-sandbox/open-email.ts:129-158 (opens the message in the reader the way a click on a list row does), calendar-navigation.ts:28-60 (a calendar view; the Tasks view only while tasks are on), open-task.ts:36-50
// (the task editor, only while the calendar is on screen and has the task), row-badges.ts:34 and host-api.ts:2310-2325 (the hooks run again for the rows, the open message and the shown list).
// The shell is moved through the paths its own controls use: ShellViewModel.TryActivate (the rail), MessageListViewModel.Select / IMessageOpener.Open (a list row), CalendarViewModel.SetView (the view switch) and TasksViewModel.OpenAsync
// (the task row). Every method runs on the UI thread and first checks that the login the add-on runs on is still the one on screen (an add-on of a login that was switched away cannot move the view).
// Deviations: a message of a shared account is refused (the reader opens the active login's own mail only); the Tasks view needs the calendar's "tasks" switch on; the opened message does not switch the list's folder.
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Navigation;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Shell;

namespace Barbican.App.Plugins;

internal sealed class PluginAppNavigator(ShellViewModel shell, IActiveMail active, MessageListViewModel list, IMessageOpener opener, MessageLoader loader,
    CalendarViewModel calendar, TasksViewModel tasks, IDispatcher dispatcher) : IAppNavigator, IPluginViewRefresh
{
    public async Task<PluginOpenEmailOutcome> OpenEmailAsync(string accountKey, string? sharedAccountId, EmailSummary summary, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var outcome = PluginOpenEmailOutcome.Refused;
        await dispatcher.InvokeAsync(() =>
        {
            if (sharedAccountId is not null || !OnScreen(accountKey) || !shell.TryActivate(AppId.Mail)) return Task.CompletedTask;
            var row = list.Messages.FirstOrDefault(m => string.Equals(m.Id, summary.Id, StringComparison.Ordinal));
            if (row is not null) list.Select(row, OpenReason.Search, ListDirection.None);
            else opener.Open(new MessageRowViewModel(summary), OpenReason.Search, ListDirection.None);
            outcome = PluginOpenEmailOutcome.Opened;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return outcome;
    }

    public async Task<bool> OpenCalendarAsync(string accountKey, PluginCalendarView view, CancellationToken ct)
    {
        var moved = false;
        await dispatcher.InvokeAsync(() =>
        {
            var kind = view switch
            {
                PluginCalendarView.Month => CalendarViewKind.Month,
                PluginCalendarView.Week => CalendarViewKind.Week,
                PluginCalendarView.Day => CalendarViewKind.Day,
                PluginCalendarView.Agenda => CalendarViewKind.Agenda,
                _ => CalendarViewKind.Tasks,
            };
            if (!OnScreen(accountKey) || !calendar.AvailableViews.Contains(kind) || !shell.TryActivate(AppId.Calendar)) return Task.CompletedTask;
            calendar.SetView(kind);
            moved = true;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return moved;
    }

    public async Task<bool> OpenTaskAsync(string accountKey, string taskId, CancellationToken ct)
    {
        var opened = false;
        await dispatcher.InvokeAsync(async () =>
        {
            if (!OnScreen(accountKey) || shell.ActiveApp != AppId.Calendar || !tasks.Contains(taskId)) return;
            await tasks.OpenAsync(taskId, ct).ConfigureAwait(true);
            opened = true;
        }).ConfigureAwait(false);
        return opened;
    }

    public Task RefreshRowBadgesAsync(CancellationToken ct) => dispatcher.InvokeAsync(list.RefreshBadgesAsync);

    public Task RerenderEmailAsync(CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        _ = loader.ReopenShown();
        return Task.CompletedTask;
    });

    public Task RerenderFetchedEmailsAsync(CancellationToken ct) => dispatcher.InvokeAsync(list.ReloadAsync);

    private bool OnScreen(string accountKey) => string.Equals(active.AccountKey, accountKey, StringComparison.Ordinal);
}
#endif
