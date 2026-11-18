using Barbican.Core.Compose;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Settings.Integration;
using Barbican.Presentation.Tests.Compose;
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Settings;

public sealed class AssistantComposerBridgeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Draft_opening_is_dispatched_and_a_cancelled_replacement_stays_unchanged(bool allow)
    {
        using var ui = new UiThread();
        var harness = new ComposerHarness();
        using var original = harness.Open();
        var composer = new FakeComposer(harness, ui, original, allow);
        var bridge = new AssistantComposerBridge(ui, composer);
        var result = await bridge.OpenAsync(new ComposeRequest("acc", ComposeKind.Reply)
        {
            SourceEmailId = "source",
            Prefill = new ComposePrefill { To = "reader@example.org", Cc = "copy@example.org", Bcc = "blind@example.org", Subject = "Reply subject", Body = "Authored reply" },
        }, TestContext.Current.CancellationToken);
        Assert.Equal(allow, result.IsOk);
        if (allow)
        {
            using var current = composer.Current!;
            Assert.Equal("reader@example.org", current.To.Text);
            Assert.Equal("copy@example.org", current.Cc.Text);
            Assert.Equal("blind@example.org", current.Bcc.Text);
            Assert.Equal("Reply subject", current.Subject);
            Assert.Contains("Authored reply", current.FallbackBody, StringComparison.Ordinal);
            Assert.Contains("Original quotation", current.FallbackBody, StringComparison.Ordinal);
        }
        else
        {
            Assert.Same(original, composer.Current);
            Assert.Equal(string.Empty, original.Subject);
        }

        Assert.Empty(harness.Drafts.Saved);
        Assert.Empty(harness.Sender.Sent);
        Assert.Empty(ui.Unhandled);
    }

    [Fact]
    public async Task Missing_composer_is_a_failure()
    {
        var bridge = new AssistantComposerBridge(new Barbican.Presentation.Common.InlineDispatcher());
        Assert.False((await bridge.OpenAsync(new ComposeRequest("acc", ComposeKind.New), TestContext.Current.CancellationToken)).IsOk);
    }

    private sealed class FakeComposer(ComposerHarness harness, UiThread ui, ComposerViewModel original, bool allow) : IComposerService
    {
        public bool IsOpen => Current is not null;
        public ComposerViewModel? Current { get; private set; } = original;
        public event EventHandler? OpenChanged { add { } remove { } }
        public Task OpenAsync(ComposeRequest request, CancellationToken ct)
        {
            Assert.True(ui.HasThreadAccess);
            if (allow) Current = harness.Open(ComposerHarness.Seed("Original quotation", ComposeKind.Reply));
            return Task.CompletedTask;
        }
        public Task OpenSeedAsync(ComposeSeed seed, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> TryCloseAsync(CancellationToken ct) => Task.FromResult(allow);
    }
}
