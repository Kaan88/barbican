using Barbican.Core.Accounts;
using Barbican.Plugins;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Addons;
using Barbican.Presentation.Settings.Sample;
using Barbican.Presentation.Settings.Ui;
using Barbican.Presentation.Tests.Accounts;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Tests.Settings.Addons;

public sealed class AddonChatViewModelTests
{
    [Fact]
    public async Task Cancellation_releases_busy_so_another_question_can_be_asked()
    {
        var screen = new SetUiViewModel(new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation());
        var vm = new AddonChatViewModel(new CancelChat(), screen);
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
        var chat = new RecordingChat();
        var vm = new AddonChatViewModel(chat, screen, new InlineDispatcher());
        await vm.InitializeAsync(TestContext.Current.CancellationToken);
        await accounts.SwitchToAsync(second.Key, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { first.Key, second.Key }, chat.Initialized, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_sample_chat_answers_and_the_panel_state_follows_it()
    {
        var screen = new SetUiViewModel(new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation());
        var vm = new AddonChatViewModel(new SampleAddonChat(), screen);
        await vm.AskAsync("hello", null, TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, vm.Error);
        Assert.Collection(vm.Turns, t => Assert.True(t.FromUser), t => Assert.Equal("Sample answer to: hello", t.Text));
        await vm.ClearAsync(TestContext.Current.CancellationToken);
        Assert.Empty(vm.Turns);
    }

    [Fact]
    public async Task A_failure_of_the_add_on_is_shown_and_releases_busy()
    {
        var screen = new SetUiViewModel(new SampleSettingValues(), new SampleSettingInfo(), new SettingsNavigation());
        var vm = new AddonChatViewModel(new FailingChat(), screen);
        await vm.AskAsync("hello", null, TestContext.Current.CancellationToken);
        Assert.Equal("The provider is down.", vm.Error);
        Assert.False(vm.Busy);
    }

    private abstract class ChatBase : IPluginChat
    {
        public event EventHandler? Changed { add { } remove { } }
        public string Title => "Test";
        public string InputPlaceholder => string.Empty;
        public string EmptyHint => string.Empty;
        public virtual Task InitializeAsync(string accountKey, CancellationToken ct) => Task.CompletedTask;
        public IReadOnlyList<PluginChatTurn> Turns(string accountKey) => [];
        public abstract Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct);
        public Task ClearAsync(string accountKey, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingChat : ChatBase
    {
        public List<string> Initialized { get; } = [];
        public override Task InitializeAsync(string accountKey, CancellationToken ct) { Initialized.Add(accountKey); return Task.CompletedTask; }
        public override Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct) => Task.FromResult(PluginChatReply.Answer(string.Empty));
    }

    private sealed class CancelChat : ChatBase
    {
        public override Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct) => Task.FromCanceled<PluginChatReply>(ct);
    }

    private sealed class FailingChat : ChatBase
    {
        public override Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct) => Task.FromResult(PluginChatReply.Failure("The provider is down."));
    }
}
