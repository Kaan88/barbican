// M6 Codex integration regression: settings writes finish on the pool, while shell consumers bind to XAML.
using Barbican.Presentation.Tests.Support;

namespace Barbican.Presentation.Tests.Shell;

public sealed class BackgroundSettingsTests
{
    [Fact]
    public async Task Background_preferences_notify_the_shell_and_sidebar_on_the_ui_dispatcher()
    {
        using var ui = new UiThread();
        ListHarness? harness = null;
        var notifications = 0;
        await ui.InvokeAsync(() =>
        {
            harness = new ListHarness(dispatcher: ui);
            harness.Shell.PropertyChanged += (_, _) =>
            {
                Assert.True(ui.HasThreadAccess);
                notifications++;
            };
            harness.Sidebar.Tags.CollectionChanged += (_, _) => Assert.True(ui.HasThreadAccess);
            return Task.CompletedTask;
        });
        using var owned = harness!;
        await Task.Run(() => owned.Prefs.Set("""{"theme":"light","emailKeywords":[{"id":"work","label":"Work","color":"blue"}]}"""), TestContext.Current.CancellationToken);
        await ui.InvokeAsync(() =>
        {
            Assert.False(owned.Shell.Dark);
            Assert.True(notifications > 0);
            return Task.CompletedTask;
        });
        Assert.Empty(ui.Unhandled);
    }
}
