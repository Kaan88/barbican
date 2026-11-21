// Test wiring of the navigation methods (ui.openEmail, openCalendar, openTask, refreshRowBadges, rerenderEmail, rerenderFetchedEmails, downloadFile): the real PluginApiDispatcher over a FakeJmap login with switchable grant and
// session, and recording doubles for the app's navigator, view refresh and file saver.
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Model;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Api.Navigation;
using Barbican.Core.Plugins.Bridge;
using Microsoft.Extensions.Time.Testing;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class PluginNavigationRig
{
    public PluginApiRig Api { get; private set; } = null!;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero));

    public Navigator Nav { get; } = new();

    public Refresh Views { get; } = new();

    public Saver Files { get; } = new();

    public static async Task<PluginNavigationRig> StartAsync(string[] permissions)
    {
        var rig = new PluginNavigationRig();
        rig.Api = await PluginApiRig.StartAsync(() =>
            PluginNavigationMethods.Create(new PluginNavigationHost(new Readers(), new PluginMailRateLimiter(rig.Time), rig.Nav, rig.Views, rig.Files)), permissions);
        return rig;
    }

    public string IdOf(string subject) =>
        Api.F.Server.Store.Emails(Api.F.Server.Store.EmailIds()).Where(e => string.Equals((string?)e["subject"], subject, StringComparison.Ordinal)).Select(e => (string)e["id"]!).First();

    public Task<PluginCallOutcome> CallAsync(string method, string args) => Api.CallAsync(method, args);

    /// <summary>The server was never reached, nothing was moved, redrawn or saved.</summary>
    public bool Untouched => Api.F.Server.ApiCalls.Count == 0 && Nav.Emails.Count == 0 && Nav.Calendars.Count == 0 && Nav.Tasks.Count == 0 && Views.Calls.Count == 0 && Files.Saved.Count == 0 && Files.Asked.Count == 0;

    private sealed class Readers : IPluginMailReaders
    {
        public Barbican.Core.Mail.IMailService Reader(IAccountSession session, string? sharedAccountId) => sharedAccountId is null ? session.Mail : throw new NotSupportedException();
    }

    internal sealed class Navigator : IAppNavigator
    {
        public List<(string Account, string? Shared, EmailSummary Summary)> Emails { get; } = [];

        public List<(string Account, PluginCalendarView View)> Calendars { get; } = [];

        public List<(string Account, string Id)> Tasks { get; } = [];

        public PluginOpenEmailOutcome EmailResult { get; set; } = PluginOpenEmailOutcome.Opened;

        public bool Answer { get; set; } = true;

        public Action? OnMove { get; set; }

        public Task<PluginOpenEmailOutcome> OpenEmailAsync(string accountKey, string? sharedAccountId, EmailSummary summary, CancellationToken ct)
        {
            Emails.Add((accountKey, sharedAccountId, summary));
            OnMove?.Invoke();
            return Task.FromResult(EmailResult);
        }

        public Task<bool> OpenCalendarAsync(string accountKey, PluginCalendarView view, CancellationToken ct)
        {
            Calendars.Add((accountKey, view));
            OnMove?.Invoke();
            return Task.FromResult(Answer);
        }

        public Task<bool> OpenTaskAsync(string accountKey, string taskId, CancellationToken ct)
        {
            Tasks.Add((accountKey, taskId));
            OnMove?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    internal sealed class Refresh : IPluginViewRefresh
    {
        public List<string> Calls { get; } = [];

        public Task RefreshRowBadgesAsync(CancellationToken ct) => RecordAsync("badges");

        public Task RerenderEmailAsync(CancellationToken ct) => RecordAsync("email");

        public Task RerenderFetchedEmailsAsync(CancellationToken ct) => RecordAsync("fetched");

        private Task RecordAsync(string kind)
        {
            Calls.Add(kind);
            return Task.CompletedTask;
        }
    }

    internal sealed class Saver : IPluginFileSaver
    {
        public List<(string Name, byte[] Content)> Saved { get; } = [];

        public List<string> Asked { get; } = [];

        public PluginSaveOutcome Result { get; set; } = PluginSaveOutcome.Saved;

        /// <summary>Runs while the dialog is "open", before the write check: flip the grant here to go stale during the dialog.</summary>
        public Action? WhileOpen { get; set; }

        public Task? Hold { get; set; }

        public async Task<PluginSaveOutcome> SaveAsync(string suggestedName, ReadOnlyMemory<byte> content, Func<CancellationToken, ValueTask<bool>> beforeWrite, CancellationToken ct)
        {
            Asked.Add(suggestedName);
            if (Hold is { } hold) await hold;
            WhileOpen?.Invoke();
            if (Result == PluginSaveOutcome.Cancelled || !await beforeWrite(ct)) return PluginSaveOutcome.Cancelled;
            if (Result == PluginSaveOutcome.Saved) Saved.Add((suggestedName, content.ToArray()));
            return Result;
        }
    }
}
