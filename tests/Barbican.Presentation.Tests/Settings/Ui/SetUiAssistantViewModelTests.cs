using Barbican.Core.Common;
using Barbican.Core.Accounts;
using Barbican.Presentation.Common;
using Barbican.Presentation.Tests.Accounts;
using Barbican.Core.Settings.Assistant;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Ui;

namespace Barbican.Presentation.Tests.Settings.Ui;

public sealed class SetUiAssistantViewModelTests
{
    [Fact]
    public async Task Cancellation_releases_busy_so_another_question_can_be_asked()
    {
        var screen = new SetUiViewModel(new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation());
        var vm = new SetUiAssistantViewModel(new CancelSession(), screen);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.AskAsync("hello", null, new CancellationToken(true)));
        Assert.False(vm.Busy);
    }

    [Fact]
    public async Task Page_and_account_switch_initialize_the_matching_history()
    {
        var first = FakeAccounts.Info("first@example.org");
        var second = FakeAccounts.Info("second@example.org");
        var accounts = new FakeAccounts();
        accounts.Set(new AccountStatus(first, true, true, true, null, null), new AccountStatus(second, false, false, true, null, null));
        var screen = new SetUiViewModel(new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation(), accounts, new InlineDispatcher());
        var session = new RecordingSession();
        var vm = new SetUiAssistantViewModel(session, screen, new InlineDispatcher());
        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        await accounts.SwitchToAsync(second.Key, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { first.Key, second.Key }, session.Initialized, StringComparer.Ordinal);
    }

    private sealed class RecordingSession : IAssistantSession
    {
        public List<string> Initialized { get; } = [];
        public event EventHandler? Changed { add { } remove { } }
        public Task InitializeHistoryAsync(string key, CancellationToken ct) { Initialized.Add(key); return Task.CompletedTask; }
        public IReadOnlyList<AssistantMessage> History(string accountKey) => [];
        public Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct) => Task.FromResult(Result<string>.Ok(string.Empty));
        public Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
    }

    private sealed class CancelSession : IAssistantSession
    {
        public event EventHandler? Changed { add { } remove { } }
        public IReadOnlyList<AssistantMessage> History(string accountKey) => [];
        public Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct) => Task.FromCanceled<Result<string>>(ct);
        public Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
    }
}
