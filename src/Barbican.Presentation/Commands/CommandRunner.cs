// Mirrors: Bulwark lib/command-registry.ts:53-96 (a command runs by id from any surface); Core/Storage/CrashLog.cs (one file per exception in the
// profile's logs folder). Fable review 3 R3-05: no surface caught what a command threw: a fire-and-forget run (shortcut, rail) lost the
// exception silently, an awaited one (menu, palette, selection bar, hover strip, sidebar) sent it to the last-chance handler. Every surface now
// runs commands through here: an exception becomes an error toast and a crash-log line, and the run ends with a failed result. A cancelled run
// says nothing. Whether a failed *result* is toasted stays the surface's choice (the actions toast their own failures). Owner menus.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Storage;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Commands;

/// <summary>Runs a command from a surface and turns what it throws into an error toast and a crash-log line.</summary>
/// <param name="toasts">Where the error toast goes; null shows nothing (tests).</param>
/// <param name="crashFolder">The profile's logs folder (<see cref="CrashLog.FolderOf"/>); null writes no log.</param>
/// <param name="time">The clock of the log file names.</param>
public sealed class CommandRunner(IToastService? toasts = null, string? crashFolder = null, TimeProvider? time = null)
{
    /// <summary>Runs <paramref name="command"/>; a failed result is toasted only when <paramref name="toastFailure"/> is set, an exception always.</summary>
    public async Task<Result<Unit>> RunAsync(IAppCommand command, CommandContext context, bool toastFailure = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        Result<Unit> result;
        try
        {
            result = await command.ExecuteAsync(context, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // the command was cancelled: nothing to say
            return Result<Unit>.Fail(new ValidationError("Cancelled."));
        }
#pragma warning disable CA1031 // the point of the runner: whatever a command throws ends here, not in the last-chance handler
        catch (Exception e)
#pragma warning restore CA1031
        {
            if (crashFolder is not null)
            {
                _ = CrashLog.Write(crashFolder, "command " + command.Id, e, fatal: false, time ?? TimeProvider.System);
            }

            var message = "That did not work: " + e.Message;
            toasts?.Show(new ToastRequest(message, IsError: true));
            return Result<Unit>.Fail(new ValidationError(message));
        }

        if (!result.IsOk && toastFailure)
        {
            toasts?.Show(new ToastRequest(result.Error?.Message ?? "That did not work.", IsError: true));
        }

        return result;
    }

    /// <summary>Starts a run without waiting for it (a key or a click that returns at once); the run still ends in the toast and the log.</summary>
    public void Start(IAppCommand command, CommandContext context, bool toastFailure = false) => _ = RunAsync(command, context, toastFailure);
}
