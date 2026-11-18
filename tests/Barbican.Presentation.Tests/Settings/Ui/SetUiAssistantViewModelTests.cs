using Barbican.Core.Common;
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

    private sealed class CancelSession : IAssistantSession
    {
        public event EventHandler? Changed { add { } remove { } }
        public IReadOnlyList<AssistantMessage> History(string accountKey) => [];
        public Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct) => Task.FromCanceled<Result<string>>(ct);
        public Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
    }
}
