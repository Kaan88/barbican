// Fable review 3 R3-05: one runner for every surface. A command that throws ends in an error toast and a crash-log line instead of being lost
// (a shortcut or the rail did not await it) or reaching the last-chance handler (menus, palette, the bars, the sidebar). A cancelled run says
// nothing; a failed result is toasted only where the surface asks for it. Owner menus.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Input;

namespace Barbican.Presentation.Tests.Commands;

public sealed class CommandRunnerTests
{
    private static readonly CommandContext Context = new(CommandSurface.Toolbar, [], null, null, null);

    [Fact]
    public async Task A_command_that_throws_ends_in_an_error_toast_and_a_crash_log_line()
    {
        var folder = Path.Combine(Path.GetTempPath(), "barbican-runner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var shown = new List<ToastRequest>();
            var runner = new CommandRunner(new Toasts(shown), folder, TimeProvider.System);

            var r = await runner.RunAsync(Throwing("io.save", new IOException("The disk is full.")), Context, ct: TestContext.Current.CancellationToken);

            Assert.False(r.IsOk);
            var toast = Assert.Single(shown);
            Assert.True(toast.IsError);
            Assert.Contains("The disk is full.", toast.Message);
            var log = Assert.Single(Directory.GetFiles(folder, "crash-*.log"));
            var text = await File.ReadAllTextAsync(log, TestContext.Current.CancellationToken);
            Assert.Contains("command io.save", text);
            Assert.Contains("The disk is full.", text);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_cancelled_run_says_nothing()
    {
        var shown = new List<ToastRequest>();
        var r = await new CommandRunner(new Toasts(shown)).RunAsync(Throwing("x", new OperationCanceledException()), Context, ct: TestContext.Current.CancellationToken);
        Assert.False(r.IsOk);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task A_failed_result_is_toasted_only_when_the_surface_asks()
    {
        var shown = new List<ToastRequest>();
        var runner = new CommandRunner(new Toasts(shown));
        var failing = new DelegateAppCommand("f", _ => true, (_, _) => Task.FromResult(Result<Unit>.Fail(new ValidationError("No."))));

        Assert.False((await runner.RunAsync(failing, Context, ct: TestContext.Current.CancellationToken)).IsOk);
        Assert.Empty(shown);

        Assert.False((await runner.RunAsync(failing, Context, toastFailure: true, TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal("No.", Assert.Single(shown).Message);
    }

    [Fact]
    public async Task A_shortcut_whose_command_throws_shows_the_error()
    {
        // ShortcutCommands.Run did not await the command: the exception was lost and nothing was shown
        var shown = new List<ToastRequest>();
        var command = Throwing(CommandIds.ShowShortcuts, new InvalidOperationException("broken"));
        var shortcuts = new ShortcutCommands(new CommandRegistry([command]), new EmptyMailSelection(), new CommandRunner(new Toasts(shown)));

        Assert.True(shortcuts.Run(CommandIds.ShowShortcuts));

        for (var i = 0; i < 200 && shown.Count == 0; i++)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        Assert.Contains("broken", Assert.Single(shown).Message);
    }

    private static DelegateAppCommand Throwing(string id, Exception e) => new(id, _ => true, (_, _) => Task.FromException<Result<Unit>>(e));

    private sealed class Toasts(List<ToastRequest> shown) : IToastService
    {
        public ToastHandle Show(ToastRequest toast)
        {
            lock (shown)
            {
                shown.Add(toast);
            }

            return default;
        }

        public void Dismiss(ToastHandle handle)
        {
        }
    }
}
